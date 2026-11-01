using Microsoft.Extensions.DependencyInjection;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Infrastructure.EfCore;
using ProPhoneService.Infrastructure.EfCore.Notifications;

namespace ProPhoneService.Api.Tests;

/// <summary>
/// Проверка: реализация INotifier выбирается по настройкам email-провайдера
/// </summary>
public class NotifierRegistrationTests
{
    [Fact]
    public void AddNotifier_WithProvider_RegistersSmtpNotifier()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new EmailOptions
        {
            Host = "smtp.example.com",
            Password = "key",
            FromAddress = "sender@example.com",
        });
        services.AddNotifier(useEmailProvider: true);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<SmtpEmailNotifier>(scope.ServiceProvider.GetRequiredService<INotifier>());
    }

    [Fact]
    public void AddNotifier_WithoutProvider_RegistersLoggingNotifier()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotifier(useEmailProvider: false);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<LoggingNotifier>(scope.ServiceProvider.GetRequiredService<INotifier>());
    }

    [Theory]
    [InlineData("smtp.resend.com", "key", "sender@example.com", true)]
    [InlineData("smtp.resend.com", null, "sender@example.com", false)]
    [InlineData("smtp.resend.com", "key", null, false)]
    [InlineData(null, "key", "sender@example.com", false)]
    public void EmailOptions_IsConfigured_RequiresHostPasswordAndSender(
        string? host,
        string? password,
        string? fromAddress,
        bool expected)
    {
        var options = new EmailOptions { Host = host, Password = password, FromAddress = fromAddress! };

        Assert.Equal(expected, options.IsConfigured);
    }

    [Theory]
    [InlineData(465, true)]
    [InlineData(587, false)]
    [InlineData(2465, false)]
    public void EmailOptions_UseImplicitTls_OnlyForPort465(int port, bool expected)
    {
        Assert.Equal(expected, new EmailOptions { Port = port }.UseImplicitTls);
    }
}
