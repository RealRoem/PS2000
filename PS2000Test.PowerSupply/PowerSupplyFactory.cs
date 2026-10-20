namespace PS2000Test.PowerSupply;

/// <summary>
/// The only place in the solution that knows the concrete power supply types.
/// Callers ask for an <see cref="IPowerSupply"/> and never see the concrete type.
/// </summary>
public static class PowerSupplyFactory
{
    public static IPowerSupply Create(string portName) => new Ps2000PowerSupply(portName);

    public static IPowerSupply CreateDummy() => new DummyPowerSupply();

    public static IPowerSupply CreateMqtt(string brokerHost, string psuId) => new MqttPowerSupply(brokerHost, psuId);
}
