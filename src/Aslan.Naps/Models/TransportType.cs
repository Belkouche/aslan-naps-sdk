namespace Aslan.Naps.Models;

/// <summary>
/// Terminal transport type.
/// </summary>
public enum TransportType
{
    /// <summary>Sunmi P2/P3 via TCP socket. Binary TLV protocol.</summary>
    TcpSocket,

    /// <summary>Ingenico Lane/3000 via USB serial (CDC ACM). ASCII LTV protocol.</summary>
    UsbSerial
}
