using PS2000Test.PowerSupply;

namespace PS2000Test.Ui;

/// <summary>Holds the power supply the user picked on the start page for the dashboard to use.</summary>
public sealed class PowerSupplySelector
{
    public IPowerSupply? Current { get; private set; }

    public bool IsDummy { get; private set; }

    public void Select(IPowerSupply device, bool isDummy)
    {
        Clear();
        Current = device;
        IsDummy = isDummy;
    }

    public void Clear()
    {
        Current?.Dispose();
        Current = null;
    }
}
