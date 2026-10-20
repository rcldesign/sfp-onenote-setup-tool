using Sfp.Core.Plan;

namespace Sfp.OneNote.Tests;

/// <summary>Placeholder until M3.2 adds the XML builder tests.</summary>
public class PlaceholderTests
{
    [Fact]
    public void Page_handle_carries_its_key()
    {
        NotebookHandle nb = new("{nb}", @"C:\Notebooks\SFP Q1 2027", "SFP Q1 2027");
        SectionHandle s = new("{s}", "Week 02 (Jan 4 to Jan 10)", nb);
        PageHandle p = new("{p}", PageKey.Day(new DateOnly(2027, 1, 4)), s);
        p.Key.Value.Should().Be("day:2027-01-04");
    }
}
