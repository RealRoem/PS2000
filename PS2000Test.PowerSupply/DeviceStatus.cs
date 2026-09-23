namespace PS2000Test.PowerSupply;

/// <summary>Live status snapshot read from a power supply.</summary>
public readonly record struct DeviceStatus(bool RemoteControlActive, bool OutputActive, double VoltagePercent);
