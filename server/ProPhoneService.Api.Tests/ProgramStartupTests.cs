using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProPhoneService.Api.Tests;

public class ProgramStartupTests
{
    private static WebApplicationFactory<Program> Factory(string environment, Action<IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting(WebHostDefaults.EnvironmentKey, environment);
            host.UseSetting("ConnectionStrings:Default", "Host=unused");
            host.UseSetting("Database:MigrateOnStartup", "false");
            host.UseSetting("Auth:MasterEmail", "");
            host.UseSetting("Jwt:Key", new string('p', 48));
            host.UseSetting("Messengers:Telegram:Token", "123:test-token");
            host.UseSetting("Messengers:Telegram:BotUsername", "test_bot");
            host.UseSetting("Messengers:Telegram:WebhookSecret", new string('t', 32));
            host.UseSetting("Messengers:Vk:Token", "vk-test-token");
            host.UseSetting("Messengers:Vk:GroupId", "123");
            host.UseSetting("Messengers:Vk:WebhookSecret", new string('v', 32));
            host.UseSetting("Messengers:Vk:ConfirmationCode", "confirmation");
            configure?.Invoke(host);
        });

    [Fact]
    public void Startup_ProductionWithoutConnectionString_FailsFast()
    {
        using var factory = Factory("Production", host => host.UseSetting("ConnectionStrings:Default", ""));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Строка подключения", exception.Message);
    }

    [Fact]
    public async Task Startup_ProductionWithMessengers_Starts()
    {
        using var factory = Factory("Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Telegram")]
    [InlineData("Vk")]
    public void Startup_ProductionWithoutMessengerSettings_FailsFast(string channel)
    {
        using var factory = Factory("Production", host => host.UseSetting($"Messengers:{channel}:Token", ""));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Messengers", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Startup_DevelopmentWithBlankJwtKey_UsesFallback(string key)
    {
        using var factory = Factory("Development", host => host.UseSetting("Jwt:Key", key));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Startup_ShortJwtKey_FailsBeforeServingRequests()
    {
        using var factory = Factory("Development", host => host.UseSetting("Jwt:Key", new string('x', 31)));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("32", exception.Message);
    }

    [Fact]
    public async Task Startup_DevelopmentWithoutConnectionStringAndMessengers_Starts()
    {
        using var factory = Factory("Development", host =>
        {
            host.UseSetting("ConnectionStrings:Default", "");
            host.UseSetting("Messengers:Telegram:Token", "");
            host.UseSetting("Messengers:Vk:Token", "");
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
