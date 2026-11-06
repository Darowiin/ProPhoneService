namespace ProPhoneService.Application.Services;

/// <summary>
/// Исход операции авторизации: успешный (с сессией) или ошибка со статусом и текстом
/// </summary>
/// <param name="Tokens">Выданная сессия (только при успехе)</param>
/// <param name="StatusCode">HTTP-статус для ошибки (задается контроллером)</param>
/// <param name="ErrorTitle">Заголовок ошибки</param>
/// <param name="ErrorDetail">Детальное описание ошибки</param>
public sealed record AuthOutcome(
    AuthTokens? Tokens,
    int StatusCode,
    string? ErrorTitle,
    string? ErrorDetail)
{
    /// <summary>
    /// Успешный исход с сессией
    /// </summary>
    public static AuthOutcome Success(AuthTokens tokens) => new(tokens, 200, null, null);

    /// <summary>
    /// Успешный исход без создаваемых данных (204)
    /// </summary>
    public static AuthOutcome SuccessNoContent() => new(null, 204, null, null);

    /// <summary>
    /// Ошибка со статусом и описанием
    /// </summary>
    public static AuthOutcome Failure(int statusCode, string title, string detail) =>
        new(null, statusCode, title, detail);
}
