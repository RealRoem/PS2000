namespace PS2000Test.PowerSupply;

/// <summary>
/// Topic layout for one PSU, following the course example's <c>/PSU/PSU2000/{id}/...</c> scheme.
/// Agent and UI both build topics here so the two sides can't drift apart.
/// </summary>
public sealed class PsuMqttTopics(string psuId)
{
    private string Prefix { get; } = $"/PSU/PSU2000/{psuId}/";

    public string Info => Prefix + "info";

    public string Status => Prefix + "status";

    public string Online => Prefix + "online";

    // Commands are never retained: a retained command would be replayed when the agent
    // restarts and could switch the output on or change the voltage unasked.
    public string CommandVoltage => Prefix + "cmd/voltage";

    public string CommandOutput => Prefix + "cmd/output";

    public string CommandRemote => Prefix + "cmd/remote";

    public string All => Prefix + "#";
}
