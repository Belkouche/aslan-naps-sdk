# Aslan.Naps — C# SDK for NAPS Payment Terminals

C# SDK for NAPS payment terminals in Morocco. Supports two terminal models over a single unified API:

| Terminal | Connection | Transport |
|---|---|---|
| Sunmi P2 / P3 | WiFi / Ethernet (TCP) | `TransportType.TcpSocket` |
| Ingenico Lane/3000 | USB cable | `TransportType.UsbSerial` |

**Targets:** .NET 9, .NET 10, .NET Framework 4.8

---

## Installation

### NuGet (library)

```bash
dotnet add package Aslan.Naps
```

### From source

```bash
git clone https://github.com/Belkouche/aslan-naps-sdk.git
```

---

## Quickstart — SDK integration

### 1. Add the using

```csharp
using Aslan.Naps;
using Aslan.Naps.Models;
```

### 2. Create a client

**Sunmi P2/P3 over TCP (WiFi/Ethernet):**

```csharp
using var client = new NapsClient(new NapsClientOptions
{
    Transport = TransportType.TcpSocket,
    TcpHost   = "192.168.1.100",  // terminal IP address
    TcpPort   = 4444              // default NAPS port
});
```

**Ingenico Lane/3000 over USB (auto-detect port):**

```csharp
using var client = new NapsClient(TransportType.UsbSerial);
```

**Ingenico Lane/3000 over USB (force a specific port):**

```csharp
using var client = new NapsClient(new NapsClientOptions
{
    Transport      = TransportType.UsbSerial,
    SerialPortName = "COM3"          // Windows
    // SerialPortName = "/dev/cu.usbmodem1201"  // macOS/Linux
});
```

### 3. Connect

```csharp
await client.ConnectAsync();
```

### 4. Run a payment

```csharp
var result = await client.PayAsync(amount: 150.00m);

if (result.IsSuccess)
{
    Console.WriteLine($"Approved");
    Console.WriteLine($"  Card      : {result.CardNumber}");
    Console.WriteLine($"  Auth      : {result.AuthorizationNumber}");
    Console.WriteLine($"  STAN      : {result.Stan}");
    Console.WriteLine($"  Date/Time : {result.TransactionDate} {result.TransactionTime}");
}
else if (result.IsCancelled)
{
    Console.WriteLine("Cancelled by cardholder");
}
else
{
    Console.WriteLine($"Declined — RC={result.ResponseCode}");
    if (result.ShouldRetry)
        Console.WriteLine("Transient error — safe to retry");
}
```

---

## Full example

```csharp
using Aslan.Naps;
using Aslan.Naps.Models;

using var client = new NapsClient(new NapsClientOptions
{
    Transport = TransportType.TcpSocket,
    TcpHost   = "192.168.25.45",   // terminal IP address
    TcpPort   = 4444,              // default NAPS port

    RegisterId = "01",
    CashierId  = "00001",

    TcpConnectTimeoutMs  = 120_000,  // 2 min (default)
    PaymentTimeoutMs     = 120_000,  // 2 min (default)
    ConfirmationTimeoutMs =  40_000, // 40 s  (default — protocol deadline)
    TestTimeoutMs        =  30_000,  // 30 s  (default)
    ReferencingTimeoutMs =  60_000,  // 1 min (default)
});

// ── Connect ──────────────────────────────────────────────────────────────────
try
{
    await client.ConnectAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Could not reach terminal: {ex.Message}");
    return;
}

// ── Payment ──────────────────────────────────────────────────────────────────
PaymentResult pay;
try
{
    pay = await client.PayAsync(75.50m);
}
catch (TimeoutException)
{
    Console.WriteLine("Payment timed out — no response from terminal");
    return;
}
catch (Exception ex)
{
    Console.WriteLine($"Payment error: {ex.Message}");
    return;
}

if (pay.IsSuccess)
{
    Console.WriteLine("Approved");
    Console.WriteLine($"  Auth # : {pay.AuthorizationNumber}");
    Console.WriteLine($"  STAN   : {pay.Stan}");
    Console.WriteLine($"  Card   : {pay.CardNumber}");
    Console.WriteLine($"  Date   : {pay.TransactionDate} {pay.TransactionTime}");
    if (pay.ReceiptLines != null)
        foreach (var line in pay.ReceiptLines)
            Console.WriteLine(line.Text);
}
else if (pay.IsCancelled)
{
    Console.WriteLine("Cancelled by cardholder");
}
else
{
    Console.WriteLine($"Declined — RC={pay.ResponseCode} {pay.ResponseMessage}");
    if (pay.ShouldRetry)
        Console.WriteLine("Transient error — safe to retry");
}

// ── Void by STAN ─────────────────────────────────────────────────────────────
if (pay.Stan != null)
{
    try
    {
        var cancel = await client.CancelAsync(stan: pay.Stan);
        Console.WriteLine(cancel.IsSuccess ? "Voided" : $"Void failed RC={cancel.ResponseCode}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Cancel error: {ex.Message}");
    }
}

// ── Other operations ─────────────────────────────────────────────────────────
try
{
    var dup = await client.DuplicateReceiptAsync();
    Console.WriteLine(dup.ReceiptText);

    var totals = await client.TotalsAsync();
    Console.WriteLine(totals.ReceiptText);

    var refResult = await client.ReferencingAsync();
    Console.WriteLine(refResult.IsSuccess ? "Parameters loaded" : "Referencing failed");

    var test = await client.NetworkTestAsync();
    Console.WriteLine(test.Message);   // "Network OK" or error detail

    await client.ResetPinPadAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Terminal error: {ex.Message}");
}

// ── Ingenico Lane/3000 (USB) ─────────────────────────────────────────────────

// Static utilities — no connection required
string?  detected = NapsClient.FindPort();
string[] all      = NapsClient.ListPorts();

try
{
    using var usb = new NapsClient(TransportType.UsbSerial);
    await usb.ConnectAsync();
    var usbPay = await usb.PayAsync(20.00m);
    Console.WriteLine(usbPay.IsSuccess ? $"Approved {usbPay.AuthorizationNumber}" : $"RC={usbPay.ResponseCode}");
}
catch (Exception ex)
{
    Console.WriteLine($"USB terminal error: {ex.Message}");
}
```

---

## API reference

### `NapsClient`

```csharp
// Constructor
new NapsClient(NapsClientOptions options)
new NapsClient(TransportType transport)   // shorthand with defaults

// Lifecycle
await client.ConnectAsync(CancellationToken ct = default)
client.Disconnect()
client.Dispose()           // or use 'using'
bool client.IsConnected

// Transactions
Task<PaymentResult>     PayAsync(decimal amountMad, string? orderId = null, CancellationToken ct = default)
Task<PaymentResult>     CancelAsync(string stan, CancellationToken ct = default)
Task<PaymentResult>     DuplicateReceiptAsync(CancellationToken ct = default)
Task<TestResult>        NetworkTestAsync(CancellationToken ct = default)
Task<ReferencingResult> ReferencingAsync(CancellationToken ct = default)
Task<TotalsResult>      TotalsAsync(CancellationToken ct = default)
Task                    ResetPinPadAsync(CancellationToken ct = default)

// Static (USB serial only)
static string?   NapsClient.FindPort()
static string[]  NapsClient.ListPorts()
```

### `NapsClientOptions`

| Property | Type | Default | Description |
|---|---|---|---|
| `Transport` | `TransportType` | `UsbSerial` | `TcpSocket` or `UsbSerial` |
| `TcpHost` | `string?` | `null` | Terminal IP address (TCP only) |
| `TcpPort` | `int` | `4444` | TCP port (TCP only) |
| `TcpConnectTimeoutMs` | `int` | `120000` | TCP connect timeout ms |
| `SerialPortName` | `string?` | `null` | Port name — `null` = auto-detect (USB only) |
| `SerialBaudRate` | `int` | `115200` | Serial baud rate (USB only) |
| `RegisterId` | `string` | `"01"` | 2-digit register ID for NAPS NCAI field |
| `CashierId` | `string` | `"00001"` | 5-digit cashier ID for NAPS NCAI field |
| `PaymentTimeoutMs` | `int` | `120000` | Phase-1 payment read timeout ms |
| `ConfirmationTimeoutMs` | `int` | `40000` | Phase-2 confirmation read timeout ms |
| `TestTimeoutMs` | `int` | `30000` | Network test / cancel timeout ms |
| `ReferencingTimeoutMs` | `int` | `60000` | Referencing timeout ms |

### `PaymentResult`

| Property | Type | Description |
|---|---|---|
| `IsSuccess` | `bool` | `true` when terminal returned RC=000 and confirmation succeeded |
| `IsCancelled` | `bool` | `true` when cardholder cancelled (RC 280, 480, 099) |
| `ShouldRetry` | `bool` | `true` on transient errors (RC 909, 911, T01) |
| `ResponseCode` | `string?` | NAPS 3-digit response code (`"000"` = approved) |
| `ResponseMessage` | `string?` | Raw DP receipt text from terminal |
| `AuthorizationNumber` | `string?` | NA field — 6-digit auth number |
| `ApprovalCode` | `string?` | Same as `AuthorizationNumber` |
| `Stan` | `string?` | System Trace Audit Number — use for void/cancel |
| `Rrn` | `string?` | Retrieval Reference Number |
| `CardNumber` | `string?` | Masked PAN — first 6 + last 4 digits |
| `CardScheme` | `string?` | Card network identifier |
| `CardholderName` | `string?` | Cardholder name from terminal |
| `TerminalId` | `string?` | Terminal identifier |
| `TransactionDate` | `string?` | `DDMMYYYY` |
| `TransactionTime` | `string?` | `HHMMSS` |
| `ReceiptText` | `string?` | Raw receipt string |
| `ReceiptLines` | `List<ReceiptLine>?` | Structured receipt lines parsed from DP field |
| `MerchantName` | `string?` | Merchant name |
| `MerchantCity` | `string?` | Merchant city |

---

## CLI (no code required)

Install once:

```bash
# .NET 9 or 10 required
dotnet tool install --global Aslan.Naps.Cli
```

Or run from source:

```bash
git clone https://github.com/Belkouche/aslan-naps-sdk.git
cd aslan-naps-sdk
dotnet run --project src/Aslan.Naps.Cli -c Release -- <command> [options]
```

### Commands

| Command | Description |
|---|---|
| `pay <amount>` | Process a payment (MAD, e.g. `10.00`) |
| `cancel <stan>` | Void a transaction by STAN |
| `test` | Ping the terminal |
| `ref` | Load merchant parameters |
| `totals` | End-of-day settlement totals |
| `duplicate` | Reprint last receipt |
| `reset` | Reset the PIN pad module |
| `ports` | List available serial ports (USB only) |
| `debug [amount]` | Print raw TLV message and hex bytes (no terminal needed) |

### Transport flags

| Flag | When to use |
|---|---|
| `--tcp <ip>:<port>` | Sunmi P2/P3 over WiFi/Ethernet |
| `--usb` | Ingenico Lane/3000 over USB (default) |
| `--port <name>` | Force a specific serial port |

### Examples

```bash
# Sunmi P2/P3 — pay 150 MAD
lane3000 pay 150.00 --tcp 192.168.1.100:4444

# Ingenico Lane/3000 — pay (auto-detect USB port)
lane3000 pay 150.00

# Force a specific port
lane3000 pay 150.00 --port COM3
lane3000 pay 150.00 --port /dev/cu.usbmodem1201

# List ports, then ping
lane3000 ports
lane3000 test --tcp 192.168.1.100:4444

# Void by STAN
lane3000 cancel 000123 --tcp 192.168.1.100:4444
```

---

## Notes

**Lane/3000 USB reconnect:** the Ingenico Lane/3000 resets its USB interface during every network call. The SDK detects the disconnect, re-discovers the port, and reconnects automatically — no action needed from your code.

**Cancellation tokens:** all async methods accept an optional `CancellationToken`. Pass one to enforce your own deadline on top of the built-in timeouts.

**Thread safety:** `NapsClient` is not thread-safe. Do not call methods concurrently on the same instance. Use one instance per terminal, one transaction at a time.

---

## Build & test

```bash
dotnet build
dotnet test
dotnet pack src/Aslan.Naps -c Release
```

---

## License

Proprietary — Aslan Fintech / ASLANPAY SARL
