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

        var rawBytes = new List<byte>(4096);
        var buf = new byte[4096];

        // First read — block until data arrives (outer timeout via cts)
        int count;
        try
        {
            count = await stream.ReadAsync(buf, 0, buf.Length, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"No response from terminal within {timeoutMs}ms");
        }

        if (count == 0)
            throw new IOException("Connection closed before response");

        rawBytes.AddRange(new ArraySegment<byte>(buf, 0, count));
        var done = buf[count - 1] == (byte)'?';

        // Drain remaining chunks with a 1-second inter-chunk silence fallback.
        // Some terminal responses (cancel, decline) omit the '?' terminator.
        // Link to outer cts so the drain cannot extend past the overall operation deadline.
        while (!done)
        {
            using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            drainCts.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                count = await stream.ReadAsync(buf, 0, buf.Length, drainCts.Token);
                if (count == 0) break;
                rawBytes.AddRange(new ArraySegment<byte>(buf, 0, count));
                done = buf[count - 1] == (byte)'?';
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                break; // 1-second silence — treat as end-of-message
            }
        }

        var response = Encoding.ASCII.GetString(rawBytes.ToArray());

        // Strip '!' separators inside receipt lines; replace trailing '?' with '!'
        // so LtvProtocol.ParseMessage sees the standard message terminator.
        return response.Replace("!", "").TrimEnd('?') + "!";
    }

    public void Dispose() => Disconnect();
}
