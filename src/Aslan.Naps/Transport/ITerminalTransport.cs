namespace Aslan.Naps.Transport;

public interface ITerminalTransport : IDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken ct = default);
    void Disconnect();
    Task<string> SendReceiveAsync(string message, int timeoutMs, CancellationToken ct = default);
}
