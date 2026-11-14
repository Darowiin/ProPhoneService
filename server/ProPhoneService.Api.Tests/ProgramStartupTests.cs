using System.Net;
using Microsoft.AspNetCore.TestHost;
using ProPhoneService.Infrastructure.EfCore.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using static ProPhoneService.Api.Tests.MessengerBindingPersistenceTests;

namespace ProPhoneService.Api.Tests;

public class ProgramStartupTests
{
    private static WebApplicationFactory<Program> Factory(string environment, Action<IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting(WebHostDefaults.EnvironmentKey, environment);
            host.UseSetting("ConnectionStrings:Default", "Host=unused");
            host.UseSetting("Database:MigrateOnStartup", "false");
            host.UseSetting("Auth:MasterPhone", "");
            host.UseSetting("Jwt:Key", new string('p', 48));
            host.UseSetting("Messengers:Vk:Token", "vk-test-token");
            host.UseSetting("Messengers:Vk:GroupId", "123");
            host.ConfigureTestServices(services =>
            {
                var polling = services.Single(x => x.ImplementationType == typeof(VkLongPollService));
                services.Remove(polling);

                var expiration = services.Single(x => x.ImplementationType == typeof(PendingOrderExpirationService));
                services.Remove(expiration);
            });
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

    [PostgresTheory]
    [InlineData("Development", true)]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    [InlineData("Production", false)]
    public async Task Startup_MigrationsFollowExplicitSetting(string environment, bool migrateOnStartup)
    {
        await using var database = await Database.CreateAsync(migrate: false);
        await using var context = database.Context();
        using var factory = Factory(environment, host =>
        {
            host.UseSetting("ConnectionStrings:Default", context.Database.GetConnectionString());
            host.UseSetting("Database:MigrateOnStartup", migrateOnStartup.ToString());
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var applied = await context.Database.GetAppliedMigrationsAsync();
        if (migrateOnStartup)
        {
            Assert.NotEmpty(applied);
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        }
        else
        {
            Assert.Empty(applied);
        }
    }

    [Theory]
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
            host.UseSetting("Messengers:Vk:Token", "");
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
