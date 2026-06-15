using System.IO.Ports;
using Aslan.Naps.Models;
using Aslan.Naps.Protocol;

namespace Aslan.Naps.Transport;

/// <summary>
/// USB serial transport for Ingenico Lane/3000.
/// Handles USB CDC resets via a background reader thread that can reconnect
/// to the port if the device temporarily disconnects during a transaction.
/// </summary>
public class SerialTransport : ITerminalTransport
{
    private readonly NapsClientOptions _options;
    private SerialPort? _port;
    private readonly object _bufferLock = new();
    private readonly List<byte> _rxBuffer = new();

    public SerialTransport(NapsClientOptions options) => _options = options;

    public bool IsConnected => _port?.IsOpen == true;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        var portName = _options.SerialPortName ?? PortDiscovery.FindIngenicoPort();
        if (portName == null)
            throw new InvalidOperationException("Ingenico Lane/3000 not found. Check USB connection.");

        _port = new SerialPort(portName, _options.SerialBaudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 50,
            WriteTimeout = 5000,
            Handshake = Handshake.None,
            DtrEnable = false,
            RtsEnable = false
        };
        _port.Open();
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        try { _port?.Close(); } catch { /* port may already be dead */ }
        _port?.Dispose();
        _port = null;
    }

    public async Task<string> SendReceiveAsync(string message, int timeoutMs, CancellationToken ct = default)
    {
        if (_port == null || !_port.IsOpen)
            throw new InvalidOperationException("Not connected");

        lock (_bufferLock) _rxBuffer.Clear();

        var stopEvent = new ManualResetEventSlim(false);
        var readerThread = new Thread(() => ReaderLoop(stopEvent))
        {
            IsBackground = true,
            Name = "Lane3000-Reader"
        };
        readerThread.Start();

        // Give reader thread time to start
        await Task.Delay(50, ct);

        // Convert ASCII LTV message to binary TLV and send with 2-byte length prefix
        _port.DiscardInBuffer();
        var fields = LtvProtocol.ParseMessage(message);
        var tm = fields.GetValueOrDefault(LtvProtocol.TagTm, "");
        var txnType = tm switch
        {
            "001" => BinaryTlvProtocol.TxnTypeSale,
            "003" => BinaryTlvProtocol.TxnTypeReversal,
            _ => BinaryTlvProtocol.TxnTypeSale
        };
        var amount = fields.GetValueOrDefault(LtvProtocol.TagMt, "0");
        var reference = fields.GetValueOrDefault(LtvProtocol.TagStan, fields.GetValueOrDefault(LtvProtocol.TagNs, ""));
        var tlvData = BinaryTlvProtocol.BuildRequest(txnType, amount, reference);
        var packet = BinaryTlvProtocol.WrapWithLength(tlvData);
        _port.Write(packet, 0, packet.Length);

        // Wait for 2-byte length prefix in buffer, then read the full response
        var startTick = Environment.TickCount;
        while (unchecked(Environment.TickCount - startTick) < timeoutMs && !ct.IsCancellationRequested)
        {
            lock (_bufferLock)
            {
                if (_rxBuffer.Count >= 2) break;
            }
            await Task.Delay(20, ct);
        }

        // Wait for the full response payload
        int responseLen = 0;
        lock (_bufferLock)
        {
            if (_rxBuffer.Count >= 2)
                responseLen = (_rxBuffer[0] << 8) | _rxBuffer[1];
        }

        if (responseLen > 0)
        {
            while (unchecked(Environment.TickCount - startTick) < timeoutMs && !ct.IsCancellationRequested)
            {
                lock (_bufferLock)
                {
                    if (_rxBuffer.Count >= 2 + responseLen) break;
                }
                await Task.Delay(20, ct);
            }
        }

        stopEvent.Set();
        readerThread.Join(2000);

        byte[] responseBytes;
        lock (_bufferLock)
        {
            if (_rxBuffer.Count < 2)
                throw new TimeoutException($"No response within {timeoutMs}ms");
            var len = (_rxBuffer[0] << 8) | _rxBuffer[1];
            if (_rxBuffer.Count < 2 + len)
                throw new TimeoutException($"Incomplete response: got {_rxBuffer.Count - 2} of {len} bytes");
            responseBytes = _rxBuffer.Skip(2).Take(len).ToArray();
        }

        // Parse binary TLV response and convert to ASCII LTV for NapsClient
        var binaryFields = BinaryTlvProtocol.ParseResponse(responseBytes);
        return ConvertBinaryToLtv(binaryFields, tm);
    }

    private void ReaderLoop(ManualResetEventSlim stopEvent)
    {
        while (!stopEvent.IsSet)
        {
            try
            {
                if (_port == null || !_port.IsOpen) break;
                var bytesToRead = _port.BytesToRead;
                if (bytesToRead > 0)
                {
                    var buf = new byte[bytesToRead];
                    var read = _port.Read(buf, 0, bytesToRead);
                    if (read > 0)
                    {
                        lock (_bufferLock)
                        {
                            for (var i = 0; i < read; i++)
                                _rxBuffer.Add(buf[i]);
                        }
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
            catch (Exception)
            {
                // Port died (USB CDC reset) -- try to reconnect
                try { _port?.Close(); } catch { /* ignore */ }

                while (!stopEvent.IsSet)
                {
                    var newPort = PortDiscovery.FindIngenicoPort();
                    if (newPort != null)
                    {
                        try
                        {
                            _port = new SerialPort(newPort, _options.SerialBaudRate, Parity.None, 8, StopBits.One)
                            {
                                ReadTimeout = 50,
                                Handshake = Handshake.None
                            };
                            _port.Open();
                            break;
                        }
                        catch { /* retry */ }
                    }
                    Thread.Sleep(100);
                }
            }
        }
    }

    private static string ConvertBinaryToLtv(Dictionary<byte, string> binaryFields, string requestTm)
    {
        var parts = new List<string>();
        if (int.TryParse(requestTm, out var tmNum))
            parts.Add(LtvProtocol.Tlv(LtvProtocol.TagTm, (tmNum + 100).ToString().PadLeft(3, '0')));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagResponseCode, out var cr))
            parts.Add(LtvProtocol.Tlv(LtvProtocol.TagCr, cr));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagAuthNumber, out var auth))
            parts.Add(LtvProtocol.Tlv(LtvProtocol.TagNa, auth));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagCardNumber, out var card))
            parts.Add(LtvProtocol.Tlv(LtvProtocol.TagNcar, card));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagStan, out var stan))
            parts.Add(LtvProtocol.Tlv(LtvProtocol.TagStan, stan));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagRrn, out var rrn))
            parts.Add(LtvProtocol.Tlv("014", rrn));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagTerminalId, out var tid))
            parts.Add(LtvProtocol.Tlv("020", tid));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagApprovalCode, out var ac))
            parts.Add(LtvProtocol.Tlv(LtvProtocol.TagNa, ac));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagCardScheme, out var scheme))
            parts.Add(LtvProtocol.Tlv("018", scheme));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagTxnDate, out var date))
            parts.Add(LtvProtocol.Tlv(LtvProtocol.TagDa, date));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagTxnTime, out var time))
            parts.Add(LtvProtocol.Tlv(LtvProtocol.TagHe, time));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagResponseMsg, out var msg))
            parts.Add(LtvProtocol.Tlv(LtvProtocol.TagDp, msg));
        return string.Concat(parts) + "!";
    }

    public void Dispose() => Disconnect();
}
