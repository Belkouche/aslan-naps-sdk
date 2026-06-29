namespace Aslan.Naps.Models;

public class NapsClientOptions
{
    /// <summary>Transport type: TcpSocket (Sunmi) or UsbSerial (Lane/3000).</summary>
    public TransportType Transport { get; set; } = TransportType.UsbSerial;

    // --- TCP Socket options (Sunmi P2/P3) ---
    public string? TcpHost { get; set; }
    public int TcpPort { get; set; } = 4444;
    public int TcpConnectTimeoutMs { get; set; } = 120_000;

    // --- USB Serial options (Lane/3000) ---
    /// <summary>Serial port name. Null = auto-discover by Ingenico Vendor ID.</summary>
    public string? SerialPortName { get; set; }
    public int SerialBaudRate { get; set; } = 115200;

    // --- Common ---
    /// <summary>Register ID (2 digits) for NCAI field. Default "01".</summary>
    public string RegisterId { get; set; } = "01";
    /// <summary>Cashier ID (5 digits) for NCAI field. Default "00001".</summary>
    public string CashierId { get; set; } = "00001";
    /// <summary>Payment timeout in ms. Default 120s.</summary>
    public int PaymentTimeoutMs { get; set; } = 120_000;
    /// <summary>Network test timeout in ms. Default 30s.</summary>
    public int TestTimeoutMs { get; set; } = 30_000;
    /// <summary>Referencing timeout in ms. Default 60s.</summary>
    public int ReferencingTimeoutMs { get; set; } = 60_000;
}
