# Aslan.Naps — C# SDK for NAPS Payment Terminals

C# SDK for NAPS payment terminals in Morocco. Supports two terminal models:

| Terminal | Connection | Transport |
|----------|------------|-----------|
| Sunmi P2 / P3 | WiFi / Ethernet (TCP) | `TransportType.TcpSocket` |
| Ingenico Lane/3000 | USB cable | `TransportType.UsbSerial` |

---

## CLI — Run a payment in 3 steps

> No code required. Works on Windows, macOS, Linux.

### Step 1 — Prerequisites

- Install [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer
- Clone this repository

```bash
git clone https://github.com/Belkouche/aslan-naps-sdk.git
cd aslan-naps-sdk
```

### Step 2 — Connect your terminal

**Sunmi P2/P3 (WiFi/Ethernet):** make sure the terminal and your computer are on the same network. Note the terminal's IP address (visible in the terminal's network settings, usually under WiFi or Ethernet info).

**Ingenico Lane/3000 (USB):** plug in the USB cable. The port is auto-detected.

### Step 3 — Run a payment

#### Sunmi P2/P3 — TCP

Replace `192.168.1.100` with your terminal's actual IP address.

```bash
dotnet run --project src/Aslan.Naps.Cli/Aslan.Naps.Cli.csproj -c Release -- pay 10.00 --tcp 192.168.1.100:4444
```

Expected output:
```
Connecting (TcpSocket)... OK
Payment: 10.00 MAD...
APPROVED CR=000 Card=516794******3315 Auth=206632 STAN=123456
```

#### Ingenico Lane/3000 — USB

```bash
dotnet run --project src/Aslan.Naps.Cli/Aslan.Naps.Cli.csproj -c Release -- pay 10.00
```

If the terminal is not found automatically, list available ports first:

```bash
dotnet run --project src/Aslan.Naps.Cli/Aslan.Naps.Cli.csproj -c Release -- ports
```

Then force the correct port:

```bash
dotnet run --project src/Aslan.Naps.Cli/Aslan.Naps.Cli.csproj -c Release -- pay 10.00 --port COM3
# or on macOS/Linux:
dotnet run --project src/Aslan.Naps.Cli/Aslan.Naps.Cli.csproj -c Release -- pay 10.00 --port /dev/cu.usbmodem1201
```

### All available commands

| Command | What it does |
|---------|--------------|
| `pay <amount>` | Process a payment (amount in MAD, e.g. `10.00`) |
| `cancel <stan>` | Void a transaction using its STAN number |
| `test` | Ping the terminal to confirm the connection |
| `ref` | Load merchant parameters from the terminal |
| `totals` | Print end-of-day settlement totals |
| `duplicate` | Reprint the last transaction receipt |
| `reset` | Reset the PinPAD module |
| `ports` | List available USB serial ports (USB terminals only) |

### Transport flags

| Flag | When to use |
|------|-------------|
| `--tcp <ip>:<port>` | Sunmi P2/P3 over WiFi/Ethernet |
| `--usb` | Ingenico Lane/3000 over USB (default when no flag is given) |
| `--port <name>` | Force a specific serial port, e.g. `COM3` or `/dev/cu.usbmodem1201` |

---

## SDK (for developers)

```csharp
using Aslan.Naps;
using Aslan.Naps.Models;

// Sunmi P2/P3 via TCP
using var client = new NapsClient(new NapsClientOptions
{
    Transport = TransportType.TcpSocket,
    TcpHost = "192.168.1.100",
    TcpPort = 4444
});

// Ingenico Lane/3000 via USB
using var client = new NapsClient(TransportType.UsbSerial);

await client.ConnectAsync();

var result = await client.PayAsync(10.00m);
if (result.IsSuccess)
    Console.WriteLine($"Approved! Card={result.CardNumber} STAN={result.Stan}");
```

### Full API

```csharp
PaymentResult     result = await client.PayAsync(amount, orderId, ct);
PaymentResult     result = await client.CancelAsync(stan, ct);
TestResult        result = await client.NetworkTestAsync(ct);
ReferencingResult result = await client.ReferencingAsync(ct);
TotalsResult      result = await client.TotalsAsync(ct);
PaymentResult     result = await client.DuplicateReceiptAsync(ct);
                           await client.ResetPinPadAsync(ct);

string?  port  = NapsClient.FindPort();
string[] ports = NapsClient.ListPorts();
```

---

## Targets

- .NET 8.0+ (Windows, macOS, Linux)
- .NET Framework 4.8 (Windows legacy POS)

---

## Lane/3000 USB CDC Reset Handling

The Ingenico Lane/3000 resets its USB interface during every network call. The SDK handles this transparently — it detects the disconnect, re-discovers the port, and reconnects automatically.

---

## Build & test

```bash
dotnet build
dotnet test
dotnet pack -c Release
```

---

## License

Proprietary — Aslan Fintech / ASLANPAY SARL
