namespace ProPhoneService.Api.Tests;

/// <summary>
/// Управляемое текущее время для тестов
/// </summary>
internal class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>
    /// Текущий момент в UTC
    /// </summary>
    public DateTimeOffset UtcNow { get; set; } = now;

    /// <summary>
    /// Получить заданный момент времени
    /// </summary>
    /// <returns>Текущий момент тестовых часов</returns>
    public override DateTimeOffset GetUtcNow() => UtcNow;
}
