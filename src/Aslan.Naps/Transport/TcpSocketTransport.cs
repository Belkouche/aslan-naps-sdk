using System.Net.Sockets;
using Aslan.Naps.Models;
using Aslan.Naps.Polyfills;
using Aslan.Naps.Protocol;

namespace Aslan.Naps.Transport;

/// <summary>
/// TCP socket transport for Sunmi P2/P3 terminals.
/// Sends binary TLV wrapped in a 2-byte big-endian length prefix, receives the same format,
/// and converts to/from the ASCII LTV format used by <see cref="NapsClient"/>.
/// </summary>
public class TcpSocketTransport : ITerminalTransport
{
    private readonly NapsClientOptions _options;
    private TcpClient? _client;

    public TcpSocketTransport(NapsClientOptions options) => _options = options;

    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_options.TcpHost == null)
            throw new InvalidOperationException("TcpHost must be set for TCP socket transport");

        _client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.TcpConnectTimeoutMs);
#if NET5_0_OR_GREATER
        await _client.ConnectAsync(_options.TcpHost, _options.TcpPort, cts.Token);
#else
        // net48: ConnectAsync does not accept CancellationToken
        var connectTask = _client.ConnectAsync(_options.TcpHost, _options.TcpPort);
        var delayTask = Task.Delay(_options.TcpConnectTimeoutMs, cts.Token);
        if (await Task.WhenAny(connectTask, delayTask) != connectTask)
            throw new OperationCanceledException("TCP connect timed out");
        await connectTask; // propagate any exception
#endif
    }

    public void Disconnect()
    {
        _client?.Close();
        _client?.Dispose();
        _client = null;
    }

    /// <summary>
    /// For TCP, the message arrives in ASCII LTV format. We convert it to binary TLV,
    /// send it, receive the binary response, and convert back to ASCII LTV.
    /// This keeps the NapsClient protocol-agnostic.
    /// </summary>
    public async Task<string> SendReceiveAsync(string message, int timeoutMs, CancellationToken ct = default)
    {
        if (_client == null || !_client.Connected)
            throw new InvalidOperationException("Not connected");

        var stream = _client.GetStream();

        // Parse the ASCII LTV message to extract txn type, amount, reference
        var fields = LtvProtocol.ParseMessage(message);
        var tm = fields.GetValueOrDefault(LtvProtocol.TagTm, "");
        var amount = fields.GetValueOrDefault(LtvProtocol.TagMt, "0");
        var ns = fields.GetValueOrDefault(LtvProtocol.TagNs, "");

        // Map LTV TM to binary txn type
        var txnType = tm switch
        {
            "001" => BinaryTlvProtocol.TxnTypeSale,
            "003" => BinaryTlvProtocol.TxnTypeReversal,
            _ => BinaryTlvProtocol.TxnTypeSale
        };

        var reference = fields.GetValueOrDefault(LtvProtocol.TagStan, ns);

        // Build and send binary request
        var tlvData = BinaryTlvProtocol.BuildRequest(txnType, amount, reference);
        var packet = BinaryTlvProtocol.WrapWithLength(tlvData);
        await stream.WriteAsync(packet, 0, packet.Length, ct);
        await stream.FlushAsync(ct);

        // Read response with timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        // Read 2-byte length prefix
        var lenBuf = new byte[2];
        await stream.ReadExactlyAsync(lenBuf, cts.Token);
        var responseLen = (lenBuf[0] << 8) | lenBuf[1];

        if (responseLen <= 0 || responseLen > 4096)
            throw new InvalidDataException($"Invalid response length: {responseLen}");

        var responseBuf = new byte[responseLen];
        await stream.ReadExactlyAsync(responseBuf, cts.Token);

        // Parse binary TLV and convert to ASCII LTV format
        var binaryFields = BinaryTlvProtocol.ParseResponse(responseBuf);
        return ConvertBinaryToLtv(binaryFields, tm);
    }

    /// <summary>
    /// Convert binary TLV fields to an ASCII LTV message string.
    /// Maps binary tags to LTV tags so the NapsClient can process uniformly.
    /// </summary>
    private static string ConvertBinaryToLtv(Dictionary<byte, string> binaryFields, string requestTm)
    {
        var fields = new List<string>();

        // Response TM = request TM + 100
        if (int.TryParse(requestTm, out var tmNum))
            fields.Add(LtvProtocol.Tlv(LtvProtocol.TagTm, (tmNum + 100).ToString().PadLeft(3, '0')));

        // Map binary tags to LTV tags
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagResponseCode, out var cr))
            fields.Add(LtvProtocol.Tlv(LtvProtocol.TagCr, cr));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagAuthNumber, out var auth))
            fields.Add(LtvProtocol.Tlv(LtvProtocol.TagNa, auth));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagCardNumber, out var card))
            fields.Add(LtvProtocol.Tlv(LtvProtocol.TagNcar, card));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagStan, out var stan))
            fields.Add(LtvProtocol.Tlv(LtvProtocol.TagStan, stan));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagRrn, out var rrn))
            fields.Add(LtvProtocol.Tlv("014", rrn));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagTerminalId, out var tid))
            fields.Add(LtvProtocol.Tlv("020", tid));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagApprovalCode, out var ac))
            fields.Add(LtvProtocol.Tlv(LtvProtocol.TagNa, ac));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagCardScheme, out var scheme))
            fields.Add(LtvProtocol.Tlv("018", scheme));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagTxnDate, out var date))
            fields.Add(LtvProtocol.Tlv(LtvProtocol.TagDa, date));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagTxnTime, out var time))
            fields.Add(LtvProtocol.Tlv(LtvProtocol.TagHe, time));
        if (binaryFields.TryGetValue(BinaryTlvProtocol.TagResponseMsg, out var msg))
            fields.Add(LtvProtocol.Tlv(LtvProtocol.TagDp, msg));

        return string.Concat(fields) + "!";
    }

    public void Dispose() => Disconnect();
}
