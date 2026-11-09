namespace Urras.Core.Settings;

// "3 days ago", "last month": how long ago something happened, the way GitHub Desktop says it
public static class RelativeTime
{
    private const int MINUTE = 60;
    private const int HOUR = 60 * MINUTE;
    private const int DAY = 24 * HOUR;
    private const int MONTH_DAYS = 30;
    private const int YEAR_DAYS = 365;

    public static string Describe(DateTimeOffset when, DateTimeOffset now)
    {
        double seconds = (now - when).TotalSeconds;
        if (seconds < MINUTE)
            return "just now";
        if (seconds < HOUR)
            return Plural((int)(seconds / MINUTE), "minute");
        if (seconds < DAY)
            return Plural((int)(seconds / HOUR), "hour");

        int days = (int)(seconds / DAY);
        if (days == 1)
            return "yesterday";
        if (days < MONTH_DAYS)
            return days + " days ago";
        if (days < YEAR_DAYS)
        {
            int months = days / MONTH_DAYS;
            return months == 1 ? "last month" : months + " months ago";
        }

        int years = days / YEAR_DAYS;
        return years == 1 ? "last year" : years + " years ago";
    }

    private static string Plural(int count, string unit)
    {
        return count + " " + unit + (count == 1 ? string.Empty : "s") + " ago";
    }
}
