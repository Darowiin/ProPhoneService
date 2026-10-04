
namespace ProPhoneService.Domain.Shared;

/// <summary>
/// Настройки режима работы мастерской (рабочие часы, таймзона, рабочие дни)
/// </summary>
public sealed class WorkshopOptions
{
    /// <summary>
    /// IANA-таймзона расположения мастерской
    /// </summary>
    /// <example>Europe/Samara</example>
    public const string DefaultTimeZone = "Europe/Samara";

    /// <summary>
    /// IANA-таймзона расположения мастерской
    /// </summary>
    public string TimeZone { get; set; } = DefaultTimeZone;

    /// <summary>
    /// Время открытия мастерской (локальное время, HH:mm)
    /// </summary>
    public TimeOnly OpenAt { get; set; } = new(10, 0);

    /// <summary>
    /// Время закрытия мастерской (локальное время, HH:mm, включительно)
    /// </summary>
    public TimeOnly CloseAt { get; set; } = new(19, 0);

    /// <summary>
    /// Рабочие дни недели (ISO 8601: 1 — понедельник ... 7 — воскресенье)
    /// </summary>
    public int[] WorkingDays { get; set; } = [1, 2, 3, 4, 5];

    /// <summary>
    /// Проверить корректность настроек режима работы
    /// </summary>
    /// <returns>Сообщение об ошибке или <c>null</c>, если настройки корректны</returns>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(TimeZone))
        {
            return "Таймзона не задана";
        }

        if (!IsKnownTimeZone())
        {
            return $"Неизвестная таймзона: {TimeZone}";
        }

        if (OpenAt >= CloseAt)
        {
            return $"Время открытия ({OpenAt:HH:mm}) должно быть раньше времени закрытия ({CloseAt:HH:mm})";
        }

        if (WorkingDays is null || WorkingDays.Length == 0 || WorkingDays.Any(d => d is < 1 or > 7))
        {
            return "Рабочие дни должны быть числами от 1 (пн) до 7 (вс) и не пустыми";
        }

        return null;
    }

    /// <summary>
    /// Получить TimeZoneInfo по настроенной зоне
    /// </summary>
    public TimeZoneInfo ResolveTimeZone()
    {
        return TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
    }

    /// <summary>
    /// Проверить, что настроенная таймзона известна системе
    /// </summary>
    private bool IsKnownTimeZone()
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
    }
}
