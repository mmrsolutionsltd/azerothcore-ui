using AzerothCore_UI.Api.Models;
using AzerothCore_UI.Api.Services;
using Xunit;

namespace AzerothCore_UI.Api.Tests.Services;

public sealed class DatabaseBackupScheduleCalculatorTests
{
    [Fact]
    public void DailySchedule_ReturnsPreviousAndNextOccurrences()
    {
        var schedule = new DatabaseBackupSchedule(
            true, "Daily", "03:00", DayOfWeek.Sunday, true, 20);
        var now = new DateTime(2026, 7, 24, 14, 30, 0, DateTimeKind.Local);

        Assert.Equal(new DateTime(2026, 7, 24, 3, 0, 0, DateTimeKind.Local),
            DatabaseBackupScheduleCalculator.MostRecentLocalOccurrence(schedule, now));
        Assert.Equal(new DateTime(2026, 7, 25, 3, 0, 0, DateTimeKind.Local),
            DatabaseBackupScheduleCalculator.NextLocalOccurrence(schedule, now));
    }

    [Fact]
    public void WeeklySchedule_BeforeTodaysTime_UsesPreviousWeek()
    {
        var schedule = new DatabaseBackupSchedule(
            true, "Weekly", "20:00", DayOfWeek.Friday, true, 20);
        var now = new DateTime(2026, 7, 24, 14, 30, 0, DateTimeKind.Local);

        Assert.Equal(new DateTime(2026, 7, 17, 20, 0, 0, DateTimeKind.Local),
            DatabaseBackupScheduleCalculator.MostRecentLocalOccurrence(schedule, now));
        Assert.Equal(new DateTime(2026, 7, 24, 20, 0, 0, DateTimeKind.Local),
            DatabaseBackupScheduleCalculator.NextLocalOccurrence(schedule, now));
    }

    [Theory]
    [InlineData("3:00")]
    [InlineData("25:00")]
    [InlineData("soon")]
    public void ParseTime_RejectsInvalidFormat(string value) =>
        Assert.Throws<ArgumentException>(() => DatabaseBackupScheduleCalculator.ParseTime(value));
}
