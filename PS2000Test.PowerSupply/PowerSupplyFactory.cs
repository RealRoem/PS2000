namespace PS2000Test.PowerSupply;

/// <summary>
/// The only place in the solution that knows the concrete power supply type is a PS2000B.
/// Callers ask for an <see cref="IPowerSupply"/> and never see the concrete type.
/// </summary>
public static class PowerSupplyFactory
{
    public static IPowerSupply Create(string portName) => new Ps2000PowerSupply(portName);
}
