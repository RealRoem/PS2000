namespace PS2000Test.PowerSupply;

/// <summary>Static identity of a power supply, published once (retained) by the PSU agent.</summary>
public sealed record PsuInfo(string DeviceType, string SerialNumber, string ArticleNumber, double NominalVoltage);
