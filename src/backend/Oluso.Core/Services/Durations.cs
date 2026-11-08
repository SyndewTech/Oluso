namespace Oluso.Core.Services;

/// <summary>Durations as people say them in emails and UI copy.</summary>
public static class Durations
{
    /// <summary>"10 minutes", "1 hour", "24 hours", "7 days".</summary>
    public static string Describe(TimeSpan duration)
    {
        static string Unit(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")}";

        var minutes = (int)Math.Round(duration.TotalMinutes);
        if (minutes >= 2 * 24 * 60 && minutes % (24 * 60) == 0) return Unit(minutes / (24 * 60), "day");
        if (minutes >= 60 && minutes % 60 == 0) return Unit(minutes / 60, "hour");
        return Unit(minutes, "minute");
    }
}
