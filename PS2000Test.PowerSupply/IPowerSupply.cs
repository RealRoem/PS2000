namespace PS2000Test.PowerSupply;

/// <summary>
/// Technology-agnostic contract for a bench power supply. Callers depend only on this
/// interface, never on a concrete device implementation - swapping the physical unit means
/// writing a new class that implements this interface and pointing <see cref="PowerSupplyFactory"/>
/// at it, with no change required in any calling code.
/// </summary>
public interface IPowerSupply : IDisposable
{
    Task<string> GetDeviceTypeAsync();

    Task<string> GetSerialNumberAsync();

    Task<string> GetArticleNumberAsync();

    Task<double> GetNominalVoltageAsync();

    Task<DeviceStatus> GetStatusAsync();

    Task SetVoltageAsync(double volts);

    Task SetRemoteControlAsync(bool enabled);

    Task SetPowerOutputAsync(bool enabled);
}
