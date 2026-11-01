namespace ProPhoneService.Domain.Shared;

/// <summary>
/// Настройки отправки писем через SMTP-провайдера (Resend); пароль - только в окружении
/// </summary>
public sealed class EmailOptions
{
    /// <summary>
    /// SMTP-хост провайдера (по умолчанию Resend)
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// SMTP-порт: 465 - неявный TLS, остальные - STARTTLS
    /// </summary>
    public int Port { get; set; } = 465;

    /// <summary>
    /// Пользователь SMTP (у Resend - всегда resend)
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// Пароль SMTP - API-ключ провайдера; только в окружении
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Адрес отправителя (домен должен быть подтверждён у провайдера)
    /// </summary>
    public string FromAddress { get; set; } = "";

    /// <summary>
    /// Имя отправителя в письме
    /// </summary>
    public string FromName { get; set; } = "ProPhoneService";

    /// <summary>
    /// Признак полной настройки: хост, пароль и адрес отправителя заданы
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(Password)
        && !string.IsNullOrWhiteSpace(FromAddress);

    /// <summary>
    /// Использовать TLS; <c>false</c> - только для локального SMTP без шифрования (MailHog и т.п.)
    /// </summary>
    public bool UseTls { get; set; } = true;

    /// <summary>
    /// Порт с неявным TLS (SslOnConnect), иначе - STARTTLS
    /// </summary>
    public bool UseImplicitTls => Port == 465;
}
