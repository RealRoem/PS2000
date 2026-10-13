using System.IO.Ports;

namespace PS2000Test.PowerSupply;

public sealed record DetectedPowerSupply(string PortName, string DeviceType, string SerialNumber);

/// <summary>
/// Finds PS2000B units by querying the device-type object on every serial port and keeping
/// the ones that answer with a "PS ..." type string. Scanning is serialized because a probe
/// holds the port open until it is disposed - two scans at once (Blazor's double render)
/// would otherwise fail with "Access to the port is denied".
/// </summary>
public static class PowerSupplyScanner
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly SemaphoreSlim ScanLock = new(1, 1);

    public static async Task<IReadOnlyList<DetectedPowerSupply>> ScanAsync()
    {
        await ScanLock.WaitAsync();
        try
        {
            var found = new List<DetectedPowerSupply>();
            // Opening a macOS Bluetooth tty can block indefinitely and it is never a PSU.
            foreach (string port in SerialPort.GetPortNames().Where(p => !p.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)))
            {
                var detected = await ProbeAsync(port);
                if (detected is not null)
                {
                    found.Add(detected);
                }
            }

            return found;
        }
        finally
        {
            ScanLock.Release();
        }
    }

    private static async Task<DetectedPowerSupply?> ProbeAsync(string port)
    {
        using var probe = new Ps2000PowerSupply(port);
        try
        {
            var query = ReadIdentityAsync(probe, port);
            if (await Task.WhenAny(query, Task.Delay(ProbeTimeout)) != query)
            {
                return null;
            }

            return await query;
        }
        catch
        {
            return null; // not openable or not a PS2000 - expected for most ports
        }
    }

    private static async Task<DetectedPowerSupply?> ReadIdentityAsync(Ps2000PowerSupply probe, string port)
    {
        string type = await probe.GetDeviceTypeAsync();
        if (!type.StartsWith("PS", StringComparison.Ordinal))
        {
            return null;
        }

        return new DetectedPowerSupply(port, type, await probe.GetSerialNumberAsync());
    }
}
