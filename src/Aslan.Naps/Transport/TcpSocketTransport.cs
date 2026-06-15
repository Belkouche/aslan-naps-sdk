using System.Net.Sockets;
using System.Text;
using Aslan.Naps.Models;
using Aslan.Naps.Protocol;

namespace Aslan.Naps.Transport;

/// <summary>
/// TCP socket transport for Sunmi P2/P3 terminals.
/// Speaks the NAPS M2M ASCII TLV protocol: messages terminated by '!'.
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
        var connectTask = _client.ConnectAsync(_options.TcpHost, _options.TcpPort);
        var delayTask = Task.Delay(_options.TcpConnectTimeoutMs, cts.Token);
        if (await Task.WhenAny(connectTask, delayTask) != connectTask)
            throw new OperationCanceledException("TCP connect timed out");
        await connectTask;
#endif
    }

    public void Disconnect()
    {
        _client?.Close();
        _client?.Dispose();
        _client = null;
    }

    public async Task<string> SendReceiveAsync(string message, int timeoutMs, CancellationToken ct = default)
    {
        if (_client == null || !_client.Connected)
            throw new InvalidOperationException("Not connected");

        var stream = _client.GetStream();

        var bytes = Encoding.ASCII.GetBytes(message);
        await stream.WriteAsync(bytes, 0, bytes.Length, ct);
        await stream.FlushAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        // The terminal uses '?' as end-of-message terminator (not '!').
        // Read until '?' arrives.
        var sb = new StringBuilder();
        var buf = new byte[4096];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buf, 0, buf.Length, cts.Token);
                if (read == 0) break;
                sb.Append(Encoding.ASCII.GetString(buf, 0, read));
                if (sb.ToString().Contains('?')) break;
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"No response from terminal within {timeoutMs}ms");
        }

        // '!' appears inside receipt lines as a separator — strip them.
        // Replace trailing '?' with '!' so LtvProtocol.ParseMessage sees a standard terminator.
        return sb.ToString().Replace("!", "").TrimEnd('?') + "!";
    }

    public void Dispose() => Disconnect();
}
