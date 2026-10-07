using ProPhoneService.Domain.Services;
using ProPhoneService.Domain.Shared;

namespace ProPhoneService.Api.Tests;

public class VisitTimeValidatorTests
{
    private static readonly DateTime _nowUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static WorkshopOptions Options() => new()
    {
        TimeZone = "Europe/Samara",
        OpenAt = new TimeOnly(10, 0),
        CloseAt = new TimeOnly(19, 0),
        WorkingDays = [1, 2, 3, 4, 5],
    };

    /// <summary>
    /// Создать UTC-время по локальному времени Самары
    /// </summary>
    private static DateTime At(int day, int localHour, int localMinute = 0)
    {
        return new DateTime(2026, 10, day, localHour - 4, localMinute, 0, DateTimeKind.Utc);
    }

    [Fact]
    public void Validate_BeforeOpening_ReturnsError()
    {
        var error = VisitTimeValidator.Validate(At(5, 9, 59), _nowUtc, Options());

        Assert.NotNull(error);
        Assert.Contains("10:00", error);
    }

    [Fact]
    public void Validate_AtOpening_ReturnsNull()
    {
        Assert.Null(VisitTimeValidator.Validate(At(5, 10, 0), _nowUtc, Options()));
    }

    [Fact]
    public void Validate_AtClosing_ReturnsNull()
    {
        Assert.Null(VisitTimeValidator.Validate(At(5, 19, 0), _nowUtc, Options()));
    }

    [Fact]
    public void Validate_AfterClosing_ReturnsError()
    {
        var error = VisitTimeValidator.Validate(At(5, 19, 1), _nowUtc, Options());

        Assert.NotNull(error);
        Assert.Contains("19:00", error);
    }

    [Fact]
    public void Validate_InPast_ReturnsError()
    {
        var error = VisitTimeValidator.Validate(At(5, 12), new DateTime(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc), Options());

        Assert.NotNull(error);
        Assert.Contains("прошло", error);
    }

    [Fact]
    public void Validate_AtSameMomentAsNow_ReturnsError()
    {
        var now = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

        Assert.NotNull(VisitTimeValidator.Validate(At(5, 12), now, Options()));
    }

    [Theory]
    [InlineData(10)] // суббота
    [InlineData(11)] // воскресенье
    public void Validate_OnWeekend_ReturnsError(int day)
    {
        var error = VisitTimeValidator.Validate(At(day, 12), _nowUtc, Options());

        Assert.NotNull(error);
        Assert.Contains("выходной", error);
    }

    [Theory]
    [InlineData(5)]  // пн
    [InlineData(6)]  // вт
    [InlineData(7)]  // ср
    [InlineData(8)]  // чт
    [InlineData(9)]  // пт
    public void Validate_OnWorkingDayWithinHours_ReturnsNull(int day)
    {
        Assert.Null(VisitTimeValidator.Validate(At(day, 12, 30), _nowUtc, Options()));
    }

    [Fact]
    public void Validate_UtcIsConvertedToWorkshopLocalTime()
    {
        var visitUtc = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

        var error = VisitTimeValidator.Validate(
            visitUtc,
            _nowUtc,
            Options());

        Assert.NotNull(error);
        Assert.Contains("выходной", error);
    }

    [Fact]
    public void Validate_UtcKindIsRequired()
    {
        var local = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() =>
            VisitTimeValidator.Validate(
                local,
                _nowUtc,
                Options()));
    }

    [Fact]
    public void Validate_UnspecifiedKindIsRejected()
    {
        var unspecified = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() =>
            VisitTimeValidator.Validate(
                unspecified,
                _nowUtc,
                Options()));
    }

    [Fact]
    public void Validate_NowUtcMustBeUtc()
    {
        var nowLocal = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() =>
            VisitTimeValidator.Validate(
                At(5, 12),
                nowLocal,
                Options()));
    }
}
