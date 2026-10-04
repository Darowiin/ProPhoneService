using ProPhoneService.Domain.Shared;

namespace ProPhoneService.Domain.Services;

/// <summary>
/// Проверка предпочитаемой даты визита на соответствие режиму работы мастерской
/// </summary>
public static class VisitTimeValidator
{
    /// <summary>
    /// Проверить, что дата визита попадает в рабочие часы мастерской и не в прошлом
    /// </summary>
    /// <param name="preferredVisitAtUtc">Дата и время визита; Kind нормализуется к UTC (Unspecified трактуется как UTC)</param>
    /// <param name="nowUtc">Текущее время (UTC)</param>
    /// <param name="options">Настройки режима работы мастерской</param>
    /// <returns>Ошибка валидации или <c>null</c>, если дата корректна</returns>
    public static string? Validate(DateTime preferredVisitAtUtc, DateTime nowUtc, WorkshopOptions options)
    {
        preferredVisitAtUtc = preferredVisitAtUtc.Kind switch
        {
            DateTimeKind.Utc => preferredVisitAtUtc,
            DateTimeKind.Local => preferredVisitAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(preferredVisitAtUtc, DateTimeKind.Utc),
        };

        if (preferredVisitAtUtc <= nowUtc)
        {
            return "Выбранное время визита уже прошло";
        }

        var timeZone = options.ResolveTimeZone();
        var localVisit = TimeZoneInfo.ConvertTimeFromUtc(preferredVisitAtUtc, timeZone);

        var isoDay = localVisit.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)localVisit.DayOfWeek;
        if (!options.WorkingDays.Contains(isoDay))
        {
            return "Мастерская в этот день закрыта (выходной)";
        }

        var visitTime = TimeOnly.FromDateTime(localVisit);
        if (visitTime < options.OpenAt || visitTime > options.CloseAt)
        {
            return $"Мастерская открыта с {options.OpenAt:HH:mm} до {options.CloseAt:HH:mm} (включительно)";
        }

        return null;
    }
}
