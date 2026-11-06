using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProPhoneService.Api.Tests;

/// <summary>
/// Поведение Program.cs на старте: вне Development строка подключения обязательна,
/// в Development она может отсутствовать (endpoint-тесты), миграция пропускается
/// </summary>
public class ProgramStartupTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Startup_DevelopmentWithBlankJwtKey_UsesFallback(string key)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting(WebHostDefaults.EnvironmentKey, "Development");
            host.UseSetting("Database:MigrateOnStartup", "false");
            host.UseSetting("Jwt:Key", key);
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Startup_ShortJwtKey_FailsBeforeServingRequests()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseSetting("Jwt:Key", new string('x', 31)));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("32", exception.Message);
    }

    private const string ConnectionStringEnvKey = "ConnectionStrings__Default";

    [Fact]
    public void Startup_ProductionWithoutConnectionString_FailsFast()
    {
        Environment.SetEnvironmentVariable(ConnectionStringEnvKey, null);

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseSetting(WebHostDefaults.EnvironmentKey, "Production"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Строка подключения", exception.Message);
    }

    [Fact]
    public async Task Startup_DevelopmentWithoutConnectionString_StartsAndSkipsMigrate()
    {
        Environment.SetEnvironmentVariable(ConnectionStringEnvKey, "");
        try
        {
            using var client = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(host =>
                    host.UseSetting(WebHostDefaults.EnvironmentKey, "Development"))
                .CreateClient();

            var response = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringEnvKey, null);
        }
    }

    [Fact]
    public async Task Startup_ProductionWithConnectionStringAndJwtKey_Starts()
    {
        Environment.SetEnvironmentVariable(ConnectionStringEnvKey, "Host=x");
        Environment.SetEnvironmentVariable("Jwt__Key", new string('p', 48));
        Environment.SetEnvironmentVariable("Email__Password", "test-api-key");
        Environment.SetEnvironmentVariable("Email__FromAddress", "sender@example.com");
        try
        {
            using var client = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(host =>
                    host.UseSetting(WebHostDefaults.EnvironmentKey, "Production"))
                .CreateClient();

            var response = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringEnvKey, null);
            Environment.SetEnvironmentVariable("Jwt__Key", null);
            Environment.SetEnvironmentVariable("Email__Password", null);
            Environment.SetEnvironmentVariable("Email__FromAddress", null);
        }
    }

    [Fact]
    public void Startup_ProductionWithoutEmailSettings_FailsFast()
    {
        Environment.SetEnvironmentVariable(ConnectionStringEnvKey, "Host=x");
        Environment.SetEnvironmentVariable("Jwt__Key", new string('p', 48));
        Environment.SetEnvironmentVariable("Email__Password", null);
        try
        {
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
                host.UseSetting(WebHostDefaults.EnvironmentKey, "Production"));

            var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

            Assert.Contains("Email", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringEnvKey, null);
            Environment.SetEnvironmentVariable("Jwt__Key", null);
        }
    }
}
