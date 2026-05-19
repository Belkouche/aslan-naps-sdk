using Aslan.Naps;
using Aslan.Naps.Models;

const string Version = "1.0.0";

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine($"""
    Aslan NAPS Terminal CLI v{Version}
    Sunmi P2/P3 (TCP) + Ingenico Lane/3000 (USB Serial)

    Usage: lane3000 <command> [options]

    Commands:
      pay <amount>        Process a payment (MAD, e.g. 10.00)
      cancel <stan>       Cancel/void a transaction by STAN
      test                Network connectivity test
      ref                 Load merchant parameters (referencing)
      totals              End-of-day settlement totals
      duplicate           Reprint last transaction receipt
      reset               Reset PinPAD module
      ports               List available serial ports

    Transport:
      --usb               USB serial / Lane/3000 [default]
      --tcp <host:port>   TCP socket / Sunmi P2/P3
      --port <name>       Specify serial port (COM3, /dev/cu.usbmodem1201)

    Examples:
      lane3000 test
      lane3000 pay 10.00
      lane3000 cancel 123456
      lane3000 test --tcp 192.168.1.100:4444
    """);
    return 0;
}

var options = new NapsClientOptions();
string? command = null;
string? arg1 = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--usb":
            options.Transport = TransportType.UsbSerial;
            break;
        case "--tcp" when i + 1 < args.Length:
            options.Transport = TransportType.TcpSocket;
            var parts = args[++i].Split(':');
            options.TcpHost = parts[0];
            if (parts.Length > 1 && int.TryParse(parts[1], out var port))
                options.TcpPort = port;
            break;
        case "--port" when i + 1 < args.Length:
            options.SerialPortName = args[++i];
            break;
        default:
            if (command == null) command = args[i];
            else arg1 ??= args[i];
            break;
    }
}

if (command == "ports")
{
    Console.WriteLine("Available serial ports:");
    var ports = NapsClient.ListPorts();
    if (ports.Length == 0) Console.WriteLine("  (none)");
    else foreach (var p in ports) Console.WriteLine($"  {p}");
    var ingenico = NapsClient.FindPort();
    Console.WriteLine(ingenico != null ? $"\nIngenico detected: {ingenico}" : "\nNo Ingenico detected.");
    return 0;
}

using var client = new NapsClient(options);
try
{
    Console.Write($"Connecting ({options.Transport})... ");
    await client.ConnectAsync();
    Console.WriteLine("OK");
}
catch (Exception ex)
{
    Console.WriteLine($"FAILED: {ex.Message}");
    return 1;
}

try
{
    switch (command)
    {
        case "test":
            Console.WriteLine("Network test (TM=009)...");
            var test = await client.NetworkTestAsync();
            Console.WriteLine(test.IsSuccess ? $"OK (CR={test.ResponseCode})" : $"FAIL (CR={test.ResponseCode})");
            return test.IsSuccess ? 0 : 1;

        case "ref":
            Console.WriteLine("Referencing (TM=013)...");
            var reff = await client.ReferencingAsync();
            Console.WriteLine(reff.IsSuccess ? $"OK (CR={reff.ResponseCode})" : $"FAIL (CR={reff.ResponseCode})");
            if (reff.ReceiptText != null) { Console.WriteLine("--- Config ---"); Console.WriteLine(reff.ReceiptText); Console.WriteLine("---"); }
            return reff.IsSuccess ? 0 : 1;

        case "pay":
            if (arg1 == null || !decimal.TryParse(arg1, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amt))
            { Console.WriteLine("Usage: lane3000 pay <amount>"); return 1; }
            Console.WriteLine($"Payment: {amt:F2} MAD...");
            var pay = await client.PayAsync(amt);
            if (pay.IsSuccess)
            { Console.WriteLine($"APPROVED CR={pay.ResponseCode} Card={pay.CardNumber} Auth={pay.AuthorizationNumber} STAN={pay.Stan}"); }
            else
            { Console.WriteLine($"DECLINED CR={pay.ResponseCode} {pay.ResponseMessage}"); }
            return pay.IsSuccess ? 0 : 1;

        case "cancel":
            if (arg1 == null) { Console.WriteLine("Usage: lane3000 cancel <stan>"); return 1; }
            Console.WriteLine($"Cancel STAN={arg1}...");
            var cancel = await client.CancelAsync(arg1);
            Console.WriteLine(cancel.ResponseCode is "000" or "480" ? $"OK CR={cancel.ResponseCode}" : $"FAIL CR={cancel.ResponseCode}");
            return cancel.ResponseCode is "000" or "480" ? 0 : 1;

        case "totals":
            Console.WriteLine("Totals (TM=010)...");
            var tot = await client.TotalsAsync();
            Console.WriteLine(tot.IsSuccess ? $"OK CR={tot.ResponseCode}" : $"FAIL CR={tot.ResponseCode}");
            if (tot.ReceiptText != null) Console.WriteLine(tot.ReceiptText);
            return tot.IsSuccess ? 0 : 1;

        case "duplicate":
            Console.WriteLine("Duplicate receipt (TM=008)...");
            var dup = await client.DuplicateReceiptAsync();
            if (dup.ReceiptText != null) Console.WriteLine(dup.ReceiptText);
            return dup.IsSuccess ? 0 : 1;

        case "reset":
            Console.WriteLine("Reset PinPAD (TM=012)...");
            await client.ResetPinPadAsync();
            Console.WriteLine("OK");
            return 0;

        default:
            Console.WriteLine($"Unknown command: {command}. Use --help.");
            return 1;
    }
}
catch (TimeoutException ex) { Console.WriteLine($"Timeout: {ex.Message}"); return 1; }
catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); return 1; }
