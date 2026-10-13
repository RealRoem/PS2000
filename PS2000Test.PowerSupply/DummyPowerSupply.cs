namespace PS2000Test.PowerSupply;

/// <summary>
/// In-memory stand-in for a PS2000B so the UI can be used without hardware. Mimics the one
/// behaviour the dashboard depends on: the output voltage only follows the set point while
/// the output is enabled.
/// </summary>
internal sealed class DummyPowerSupply : IPowerSupply
{
    private const double NominalVoltage = 84.0;

    private bool _remote;
    private bool _output;
    private double _setVoltage;

    public Task<string> GetDeviceTypeAsync() => Task.FromResult("PS 2084-10B (dummy)");

    public Task<string> GetSerialNumberAsync() => Task.FromResult("DUMMY0000");

    public Task<string> GetArticleNumberAsync() => Task.FromResult("00000000");

    public Task<double> GetNominalVoltageAsync() => Task.FromResult(NominalVoltage);

    public Task<DeviceStatus> GetStatusAsync() =>
        Task.FromResult(new DeviceStatus(_remote, _output, _output ? _setVoltage / NominalVoltage * 100 : 0));

    public Task SetVoltageAsync(double volts)
    {
        _setVoltage = Math.Clamp(volts, 0, NominalVoltage);
        return Task.CompletedTask;
    }

    public Task SetRemoteControlAsync(bool enabled)
    {
        _remote = enabled;
        return Task.CompletedTask;
    }

    public Task SetPowerOutputAsync(bool enabled)
    {
        _output = enabled;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
