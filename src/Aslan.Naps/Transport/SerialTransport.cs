using System.IO.Ports;
using Aslan.Naps.Models;

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

        // Send message
        _port.DiscardInBuffer();
        var bytes = System.Text.Encoding.ASCII.GetBytes(message);
        _port.Write(bytes, 0, bytes.Length);

        // Wait for '!' terminator in buffer
        var startTick = Environment.TickCount;
        while (unchecked(Environment.TickCount - startTick) < timeoutMs && !ct.IsCancellationRequested)
        {
            lock (_bufferLock)
            {
                if (_rxBuffer.Contains((byte)'!'))
                    break;
            }
            await Task.Delay(20, ct);
        }

        stopEvent.Set();
        readerThread.Join(2000);

        string result;
        lock (_bufferLock)
        {
            result = System.Text.Encoding.ASCII.GetString(_rxBuffer.ToArray());
        }

        if (!result.Contains('!'))
            throw new TimeoutException($"No complete response within {timeoutMs}ms ({_rxBuffer.Count} bytes received)");

        return result;
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

    public void Dispose() => Disconnect();
}
