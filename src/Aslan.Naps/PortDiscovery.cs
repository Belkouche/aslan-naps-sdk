using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Aslan.Naps.Polyfills;

namespace Aslan.Naps;

/// <summary>
/// Auto-discovers the Ingenico Lane/3000 serial port.
/// Windows: scans COM ports for USB VID 0B00.
/// macOS: finds /dev/cu.usbmodem* via ioreg.
/// Linux: finds /dev/ttyACM* with matching VID.
/// </summary>
public static class PortDiscovery
{
    public const int IngenicoVendorId = 0x0B00; // 2816

    public static string? FindIngenicoPort()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return FindOnMacOs();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return FindOnWindows();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return FindOnLinux();
        return null;
    }

    private static string? FindOnMacOs()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("ioreg", "-r -c IOUSBHostDevice -l")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            foreach (var block in output.Split(new[] { "+-o " }, StringSplitOptions.None))
            {
                if (!block.Contains("\"idVendor\" = 2816") && !block.Contains("INGENICO"))
                    continue;
                var match = Regex.Match(block, "\"IOCalloutDevice\"\\s*=\\s*\"([^\"]+)\"");
                if (match.Success) return match.Groups[1].Value;
                match = Regex.Match(block, "cu\\.usbmodem\\S+");
                if (match.Success) return "/dev/" + match.Value.TrimEnd(',', '"');
            }
        }
        catch { /* ioreg not available or failed */ }

        // Fallback: newest cu.usbmodem device
        try
        {
            var candidates = Directory.GetFiles("/dev/", "cu.usbmodem*")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
            return candidates;
        }
        catch { return null; }
    }

    private static string? FindOnWindows()
    {
        // On Windows, scan registry for USB serial ports with Ingenico VID
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (key == null) return null;

            foreach (var vidPid in key.GetSubKeyNames())
            {
                if (vidPid.IndexOf("VID_0B00", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                using var vidKey = key.OpenSubKey(vidPid);
                if (vidKey == null) continue;
                foreach (var serial in vidKey.GetSubKeyNames())
                {
                    using var devKey = vidKey.OpenSubKey(serial + @"\Device Parameters");
                    var portName = devKey?.GetValue("PortName") as string;
                    if (portName != null) return portName;
                }
            }
        }
        catch { /* registry access failed */ }

        // Fallback: return first available COM port
        var ports = SerialPort.GetPortNames();
        return ports.FirstOrDefault();
    }

    private static string? FindOnLinux()
    {
        // Check /sys/class/tty/ttyACM*/device/... for VID 0B00
        try
        {
            foreach (var dir in Directory.GetDirectories("/sys/class/tty/", "ttyACM*"))
            {
                var ueventPath = Path.Combine(dir, "device", "uevent");
                if (!File.Exists(ueventPath)) continue;
                var uevent = File.ReadAllText(ueventPath);
                if (uevent.IndexOf("0B00", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "/dev/" + Path.GetFileName(dir);
            }
        }
        catch { /* sysfs not available */ }

        // Fallback
        try
        {
            var acmDevices = Directory.GetFiles("/dev/", "ttyACM*");
            return acmDevices.FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>List all available serial ports.</summary>
    public static string[] ListPorts()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var devPorts = Directory.GetFiles("/dev/", "cu.usbmodem*")
                    .Concat(Directory.GetFiles("/dev/", "cu.usbserial*"))
                    .ToArray();
                return devPorts.Length > 0 ? devPorts : SerialPort.GetPortNames();
            }
            catch { /* fallthrough */ }
        }
        return SerialPort.GetPortNames();
    }
}
