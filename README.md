# Aslan.Naps — C# SDK for NAPS Payment Terminals

Unified C# SDK for NAPS payment terminals in Morocco. One API, two transports:

| Transport | Terminal | Protocol |
|-----------|----------|----------|
| `TransportType.TcpSocket` | Sunmi P2/P3 | TCP socket, binary TLV |
| `TransportType.UsbSerial` | Ingenico Lane/3000 | USB CDC ACM, ASCII LTV |

## Quick Start

```csharp
using Aslan.Naps;
using Aslan.Naps.Models;

// Ingenico Lane/3000 via USB
using var client = new NapsClient(TransportType.UsbSerial);
await client.ConnectAsync();

var result = await client.PayAsync(10.00m);
if (result.IsSuccess)
    Console.WriteLine($"Approved! Card={result.CardNumber} Auth={result.AuthorizationNumber}");

// Sunmi P2/P3 via TCP
using var client = new NapsClient(new NapsClientOptions
{
    Transport = TransportType.TcpSocket,
    TcpHost = "192.168.1.100",
    TcpPort = 4444
});
```

## API

```csharp
// Payment (two-phase: auth + confirmation)
PaymentResult result = await client.PayAsync(amount, orderId, ct);

// Cancellation
PaymentResult result = await client.CancelAsync(stan, ct);

// Network test
TestResult result = await client.NetworkTestAsync(ct);

// Referencing (load merchant config)
ReferencingResult result = await client.ReferencingAsync(ct);

// End-of-day totals
TotalsResult result = await client.TotalsAsync(ct);

// Duplicate receipt
PaymentResult result = await client.DuplicateReceiptAsync(ct);

// Reset PinPAD
await client.ResetPinPadAsync(ct);

// Port discovery
string? port = NapsClient.FindPort();
string[] ports = NapsClient.ListPorts();
```

## CLI

```bash
lane3000 test                        # Network test
lane3000 pay 10.00                   # Process payment
lane3000 cancel 123456               # Void by STAN
lane3000 ref                         # Load merchant params
lane3000 totals                      # Settlement report
lane3000 ports                       # List serial ports
lane3000 test --tcp 192.168.1.100:4444  # TCP mode
```

## Targets

- .NET 8.0 (Windows, macOS, Linux)
- .NET Framework 4.8 (Windows legacy POS)

## Lane/3000 USB CDC Reset Handling

The Ingenico Lane/3000 resets its USB interface during every network call. The SDK handles this transparently with a background reader thread that detects the disconnect, re-discovers the port, and reconnects — no data is lost.

## Build

```bash
dotnet build
dotnet test
dotnet pack -c Release
```

## License

Proprietary — Aslan Fintech / ASLANPAY SARL
