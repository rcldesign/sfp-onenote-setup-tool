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
    /// Erases the printed strip and draws N numbers counting down from N (laid out by <see cref="StripPositions"/>),
    /// then the filled circle, centered on its number's ink (so the white digits sit in the middle of the disc), with
    /// that number in white on N - k + 1 (Corrected) or k (AsPrinted).
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

        // The disc is centered on the number's ink, not its advance box: the side bearings differ ("14" inks 0.7 pt right
        // of its advance center in the fallback face), and centering on the advance pushed the white digits toward the rim.
        double centerX = x[index] + c.InkBounds(numbers[index], slot.SizePt).CenterX;
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
    /// used exactly. Otherwise the numbers get equal air between their glyph runs, the way the printed strip is set
    /// (6.4 pt between neighbors, so a two-digit number takes a wider pitch than a one-digit one):
    /// gap = (numberSpan.to - numberSpan.from - the sum of the N widths) / (N - 1), and number k (0-based) starts at
    /// numberSpan.from plus the widths of the numbers before it plus k gaps. The first number therefore starts at
    /// numberSpan.from and the last ends at numberSpan.to; with 14 numbers the left edges still average about 13.8 pt
    /// apart. A single number starts at numberSpan.from.
    /// </summary>
    /// <param name="slot">The manifest strip: printed positions and the span to spread across.</param>
    /// <param name="numbers">The numbers left to right, as <see cref="StripNumbers"/> gives them.</param>
    /// <param name="widthPt">Advance width of a number in points at the strip size.</param>
    /// <exception cref="InvalidOperationException">The numbers are too wide for numberSpan to leave any air between them.</exception>
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

        double[] widths = numbers.Select(widthPt).ToArray();
        double span = slot.NumberSpan.To - slot.NumberSpan.From;
        double totalWidth = widths.Sum();
        double gap = (span - totalWidth) / (numbers.Count - 1);
        if (gap is not > 0)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "slots.weeksRemaining.numberSpan ({0} to {1}, {2:0.#} pt) is too narrow for {3} numbers at {4} pt: they are {5:0.#} pt wide and leave no space between them.",
                slot.NumberSpan.From,
                slot.NumberSpan.To,
                span,
                numbers.Count,
                slot.SizePt,
                totalWidth));
        }

        double[] x = new double[numbers.Count];
        double widthsBefore = 0;
        for (int k = 0; k < numbers.Count; k++)
        {
            x[k] = slot.NumberSpan.From + widthsBefore + (k * gap);
            widthsBefore += widths[k];
        }

        return x;
    }
}
