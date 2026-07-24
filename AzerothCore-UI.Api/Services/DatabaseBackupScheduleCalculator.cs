using AzerothCore_UI.Api.Models;

namespace AzerothCore_UI.Api.Services;

internal static class DatabaseBackupScheduleCalculator
{
    public static TimeOnly ParseTime(string value)
    {
        if (!TimeOnly.TryParseExact(value, "HH:mm", out var time))
            throw new ArgumentException("Backup time must use 24-hour HH:mm format.");
        return time;
    }

    public static DateTime MostRecentLocalOccurrence(
        DatabaseBackupSchedule schedule, DateTime localNow)
    {
        var time = ParseTime(schedule.LocalTime);
        var candidate = localNow.Date.Add(time.ToTimeSpan());
        if (schedule.Frequency.Equals("Weekly", StringComparison.OrdinalIgnoreCase))
        {
            var daysBack = ((int)localNow.DayOfWeek - (int)schedule.DayOfWeek + 7) % 7;
            candidate = candidate.AddDays(-daysBack);
        }
        if (candidate > localNow)
            candidate = candidate.AddDays(schedule.Frequency.Equals(
                "Weekly", StringComparison.OrdinalIgnoreCase) ? -7 : -1);
        return candidate;
    }

    public static DateTime NextLocalOccurrence(DatabaseBackupSchedule schedule, DateTime localNow)
    {
        var recent = MostRecentLocalOccurrence(schedule, localNow);
        var interval = schedule.Frequency.Equals("Weekly", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);
        var next = recent + interval;
        return next > localNow ? next : next + interval;
    }
}
