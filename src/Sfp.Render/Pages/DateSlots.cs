using System.Globalization;
using Sfp.Core;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.Core.Plan;
using SkiaSharp;

namespace Sfp.Render.Pages;

/// <summary>
/// M2.4: the date slot (baked fields, or the daily-page link with the printed slashes erased), the weeks-remaining
/// strip (erased and redrawn with N numbers and the circle) and the day counter line. All geometry comes from
/// manifest.slots.date, slots.weeksRemaining and slots.dayCounter; the text from <see cref="PlannerText"/>.
/// </summary>
internal static class DateSlots
{
    public const string DailyDateAnchor = "dailyDate";

    /// <summary>
    /// Draws the three date fields at the field anchors, or, on a page whose date is a link (slots.date.linkOn and a
    /// dailyDate link in the plan), erases the printed slashes and places the anchor instead. A day page outside the
    /// quarter is baked at the greyed ink opacity.
    /// </summary>
    public static void DrawDate(RenderContext c)
    {
        DateSlot slot = c.Manifest.Slots.Date;
        if (!c.Applies(slot.AppliesTo) || c.Page.Date is not DateOnly date)
        {
            return;
        }

        LinkSpec? link = slot.LinkOn.Contains(c.PdfPageName, StringComparer.Ordinal) ? c.Link(DailyDateAnchor) : null;
        if (link is not null)
        {
            c.Erase(slot.SlashErase);
            RightAlignedAnchor anchor = c.Manifest.Links.DailyDate;
            c.AddLink(link.AnchorId, anchor.Rect, link.Text, anchor.Align, anchor.SizePt);
            return;
        }

        DateSlotText text = PlannerText.DateSlot(date, c.Generate.DateOrder, c.Generate.PadDateFields);
        string[] fields = [text.First, text.Second, text.Third];
        double opacity = c.InQuarter ? 1.0 : c.Manifest.Colors.GreyedInkOpacity;
        for (int i = 0; i < Math.Min(fields.Length, slot.Fields.Count); i++)
        {
            DateField field = slot.Fields[i];
            c.Text(fields[i], field.X, slot.BaselineY, field.Align, slot.SizePt, opacity);
        }
    }

    /// <summary>
    /// Erases the printed strip and draws N numbers counting down from N, then the filled circle with its number in
    /// white on N - k + 1 (Corrected) or k (AsPrinted).
    /// </summary>
    public static void DrawWeeksRemaining(RenderContext c)
    {
        WeeksRemainingSlot slot = c.Manifest.Slots.WeeksRemaining;
        if (!c.Applies(slot.AppliesTo))
        {
            return;
        }

        int n = c.Page.IntToken(Tokens.Extra.WeekCount);
        int k = c.Page.IntToken(Tokens.Extra.Week);
        c.Erase(slot.Erase);

        string[] numbers = StripNumbers(n);
        IReadOnlyList<double> x = StripPositions(slot, numbers, text => c.TextWidth(text, slot.SizePt));
        for (int i = 0; i < numbers.Length; i++)
        {
            c.Text(numbers[i], x[i], slot.BaselineY, TextAlign.Left, slot.SizePt);
        }

        int circled = c.Generate.WeeksRemaining == WeeksRemainingMode.AsPrinted ? k : n - k + 1;
        int index = n - circled;
        if (index < 0 || index >= numbers.Length)
        {
            return;
        }

        double centerX = x[index] + (c.TextWidth(numbers[index], slot.SizePt) / 2);
        c.Circle(centerX, slot.CircleCenterY, slot.CircleDiameterPt);
        c.Text(numbers[index], x[index], slot.BaselineY, TextAlign.Left, slot.SizePt, SKColors.White);
    }

    /// <summary>"Day 4 of 90 · Week 2 of 14", right-aligned under the date at the counter opacity; nothing on an out-of-quarter day.</summary>
    public static void DrawDayCounter(RenderContext c)
    {
        DayCounterSlot slot = c.Manifest.Slots.DayCounter;
        if (!c.Applies(slot.AppliesTo) || !c.InQuarter)
        {
            return;
        }

        string text = PlannerText.DayCounter(
            c.Page.IntToken(Tokens.Extra.DayOfQuarter),
            c.Page.IntToken(Tokens.Extra.QuarterLength),
            c.Page.IntToken(Tokens.Extra.Week),
            c.Page.IntToken(Tokens.Extra.WeekCount),
            slot.Format);
        c.Text(text, slot.RightX, slot.BaselineY, TextAlign.Right, slot.SizePt, c.Manifest.Colors.CounterInkOpacity);
    }

    /// <summary>The strip numbers left to right: N, N - 1 ... 1.</summary>
    public static string[] StripNumbers(int n) =>
        Enumerable.Range(0, Math.Max(0, n)).Select(i => (n - i).ToString(CultureInfo.InvariantCulture)).ToArray();

    /// <summary>
    /// Left edges of the strip numbers. When the count equals the printed count the printed positions (numberX) are
    /// used exactly; otherwise the numbers are spread evenly so the first starts at numberSpan.from and the last ends
    /// at numberSpan.to (with 14 numbers the printed 14.9 pt pitch tightens to about 13.8 pt).
    /// </summary>
    public static IReadOnlyList<double> StripPositions(WeeksRemainingSlot slot, IReadOnlyList<string> numbers, Func<string, double> widthPt)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(numbers);
        ArgumentNullException.ThrowIfNull(widthPt);
        if (numbers.Count == slot.NumberX.Count)
        {
            return slot.NumberX;
        }

        if (numbers.Count <= 1)
        {
            return numbers.Count == 0 ? Array.Empty<double>() : new[] { slot.NumberSpan.From };
        }

        double pitch = (slot.NumberSpan.To - slot.NumberSpan.From - widthPt(numbers[^1])) / (numbers.Count - 1);
        return Enumerable.Range(0, numbers.Count).Select(i => slot.NumberSpan.From + (i * pitch)).ToList();
    }
}
