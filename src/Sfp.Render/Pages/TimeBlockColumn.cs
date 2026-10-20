using System.Globalization;
using Sfp.Core;
using Sfp.Core.Manifests;
using Sfp.Core.Options;

namespace Sfp.Render.Pages;

/// <summary>
/// M2.7: the Time Blocks column rebuilt for the configured window. The printed 24-row column (7 pt labels) is erased
/// and one row per hour is drawn: a rule at the top of each row (and one closing rule at the bottom), the 12-hour
/// label centered on the rule as the planner prints it, and a propagated "timeBlockEvent:{hour}" slot per row for
/// the calendar import. The column is always rebuilt, even for a 24-row window, so every page carries the 9 pt labels.
/// </summary>
internal static class TimeBlockColumn
{
    public const string EventSlot = "timeBlockEvent";

    public static void Draw(RenderContext c)
    {
        TimeBlocksSlot slot = c.Manifest.Slots.TimeBlocks;
        if (!c.Applies(slot.AppliesTo))
        {
            return;
        }

        TimeBlockWindow window = c.Generate.TimeBlocks;
        if (!window.IsValid)
        {
            throw new InvalidOperationException($"Time block window {window.StartHour}-{window.EndHour} is not valid: start 0 to 23, end 1 to 24, end after start.");
        }

        c.Erase(slot.Erase);
        int rows = window.RowCount;
        double pitch = slot.PitchFor(rows);
        double cap = c.CapHeight(slot.LabelSizePt);
        for (int row = 0; row < rows; row++)
        {
            int hour = window.StartHour + row;
            double y = slot.Top + (row * pitch);
            c.Rule(slot.ColumnX, y, slot.ColumnRight, y, slot.RuleWidthPt);
            c.Text(HourLabel(hour), slot.LabelRightX, y + (cap / 2), TextAlign.Right, slot.LabelSizePt);
            c.AddPropagated(
                EventSlot + ":" + hour.ToString(CultureInfo.InvariantCulture),
                new RectPt(slot.EventTextX, y, slot.ColumnRight - slot.EventTextX, pitch),
                slot.EventSizePt,
                c.Manifest.Colors.EventInkOpacity);
        }

        c.Rule(slot.ColumnX, slot.Bottom, slot.ColumnRight, slot.Bottom, slot.RuleWidthPt);
    }

    /// <summary>The printed 12-hour label of an hour: 0 and 12 give "12", 13 gives "1", 22 gives "10".</summary>
    public static string HourLabel(int hour) => (((hour % 24) + 11) % 12 + 1).ToString(CultureInfo.InvariantCulture);
}
