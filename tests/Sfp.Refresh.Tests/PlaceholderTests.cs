namespace Sfp.Refresh.Tests;

/// <summary>Placeholder until M4.2 adds the Setup reader tests.</summary>
public class PlaceholderTests
{
    [Fact]
    public void Empty_setup_values_have_eight_objective_slots()
    {
        SetupValues.Empty.Objectives.Should().HaveCount(8);
        SetupValues.Empty.AnyInked.Should().BeFalse();
    }
}
