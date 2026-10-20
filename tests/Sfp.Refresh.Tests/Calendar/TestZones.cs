namespace Sfp.Refresh.Tests.Calendar;

/// <summary>Fixed time zones for deterministic tests. IANA ids work on Windows too through ICU; the Windows id is the fallback.</summary>
public static class TestZones
{
    public static TimeZoneInfo NewYork => Find("America/New_York", "Eastern Standard Time");

    public static TimeZoneInfo Berlin => Find("Europe/Berlin", "W. Europe Standard Time");

    private static TimeZoneInfo Find(string ianaId, string windowsId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }
}
