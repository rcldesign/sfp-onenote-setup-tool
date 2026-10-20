using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sfp.Refresh.Calendar;

/// <summary>
/// A bundled holiday list such as templates/holidays-us.json: fixed dates (month and day, optionally shifted to the
/// nearest weekday when observed) and floating dates (month, weekday and ordinal; a negative ordinal counts from the
/// end of the month).
/// </summary>
/// <param name="Id">List id, "us-federal".</param>
/// <param name="Name">Display name.</param>
/// <param name="Holidays">The rules.</param>
public sealed record HolidayList(string Id, string Name, IReadOnlyList<HolidayRule> Holidays)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads a holiday list file.</summary>
    public static HolidayList Load(string path) => Parse(File.ReadAllText(path), path);

    /// <summary>Parses holiday list JSON. <paramref name="origin"/> names the source in error messages.</summary>
    public static HolidayList Parse(string json, string origin = "holiday list")
    {
        HolidayList? list;
        try
        {
            list = JsonSerializer.Deserialize<HolidayList>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"{origin} is not a valid holiday list: {ex.Message}", ex);
        }

        if (list is null || list.Holidays is null || string.IsNullOrWhiteSpace(list.Id))
        {
            throw new FormatException($"{origin} is not a valid holiday list: id and holidays are required.");
        }

        foreach (HolidayRule rule in list.Holidays)
        {
            rule.Validate(origin);
        }

        return list;
    }

    /// <summary>Every holiday of a calendar year with its observed date, in date order.</summary>
    public IReadOnlyList<(DateOnly Date, HolidayRule Rule)> DatesIn(int year) =>
        Holidays.Select(r => (Date: r.ObservedDateIn(year), Rule: r)).OrderBy(x => x.Date).ToList();
}

/// <summary>One holiday rule. Either <see cref="Day"/> (fixed) or <see cref="Weekday"/> plus <see cref="Ordinal"/> (floating) is set.</summary>
/// <param name="Name">Holiday name as written on the page.</param>
/// <param name="Month">Calendar month, 1 to 12.</param>
/// <param name="Day">Day of month for a fixed holiday, else null.</param>
/// <param name="Weekday">Weekday name for a floating holiday ("Monday"), else null.</param>
/// <param name="Ordinal">Which weekday of the month: 1 is the first, -1 the last.</param>
/// <param name="Observed">For a fixed holiday: a Saturday is observed on the Friday before, a Sunday on the Monday after.</param>
public sealed record HolidayRule(string Name, int Month, int? Day, string? Weekday, int? Ordinal, bool Observed)
{
    [JsonIgnore]
    public bool IsFixed => Day is not null;

    /// <summary>The nominal date of the holiday in a year, before any observed shift.</summary>
    public DateOnly NominalDateIn(int year)
    {
        if (Day is int day)
        {
            return new DateOnly(year, Month, day);
        }

        DayOfWeek weekday = Enum.Parse<DayOfWeek>(Weekday!, ignoreCase: true);
        int ordinal = Ordinal!.Value;
        if (ordinal > 0)
        {
            DateOnly first = new(year, Month, 1);
            int ahead = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
            return first.AddDays(ahead + (7 * (ordinal - 1)));
        }

        DateOnly last = new(year, Month, DateTime.DaysInMonth(year, Month));
        int back = ((int)last.DayOfWeek - (int)weekday + 7) % 7;
        return last.AddDays(-back - (7 * (-ordinal - 1)));
    }

    /// <summary>The date the holiday is observed in a year: the nominal date shifted off a weekend when <see cref="Observed"/> is set.</summary>
    public DateOnly ObservedDateIn(int year)
    {
        DateOnly d = NominalDateIn(year);
        if (!Observed || !IsFixed)
        {
            return d;
        }

        return d.DayOfWeek switch
        {
            DayOfWeek.Saturday => d.AddDays(-1),
            DayOfWeek.Sunday => d.AddDays(1),
            _ => d,
        };
    }

    internal void Validate(string origin)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new FormatException($"{origin}: a holiday has no name.");
        }

        if (Month is < 1 or > 12)
        {
            throw new FormatException($"{origin}: holiday '{Name}' has month {Month}; expected 1 to 12.");
        }

        if (Day is int day)
        {
            if (day < 1 || day > 31)
            {
                throw new FormatException($"{origin}: holiday '{Name}' has day {day}; expected 1 to 31.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(Weekday) || !Enum.TryParse<DayOfWeek>(Weekday, ignoreCase: true, out _))
        {
            throw new FormatException($"{origin}: holiday '{Name}' needs a day or a weekday name.");
        }

        if (Ordinal is null || Ordinal == 0 || Ordinal > 5 || Ordinal < -5)
        {
            throw new FormatException($"{origin}: holiday '{Name}' needs an ordinal between -5 and 5, not 0.");
        }
    }
}
