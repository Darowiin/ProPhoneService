using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

public class CreateOrderEndpointTests
{
    /// <summary>
    /// Понедельник 2026-10-05 08:00 UTC = 12:00 по Самаре (рабочий день, рабочие часы)
    /// </summary>
    private static readonly DateTimeOffset _nowUtc = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Фиксированное "сейчас" для детерминированной валидации даты визита
    /// </summary>
    private class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private class CreateOrderHarness
    {
        public Service[] Services { get; init; } = [];

        public RepairOrder? CreatedOrder { get; private set; }

        public Device? CreatedDevice { get; private set; }

        /// <summary>
        /// Коды, отправленные на email (хранилище одноразовых кодов)
        /// </summary>
        public Dictionary<string, AuthCode> AuthCodes { get; } = [];

        public List<(string Target, string Text)> Notifications { get; } = [];

        public WebApplicationFactory<Program> CreateFactory()
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseSetting("Jwt:Key", TestJwt.Key);
                host.UseSetting("Database:MigrateOnStartup", "false");
                host.ConfigureTestServices(services =>
                {
                    var deviceRepo = new Mock<IRepository<Device>>();
                    deviceRepo
                        .Setup(r => r.AddAsync(It.IsAny<Device>(), It.IsAny<CancellationToken>()))
                        .Callback((Device d, CancellationToken _) => CreatedDevice = d)
                        .Returns(Task.CompletedTask);

                    var serviceRepo = new Mock<IServiceRepository>();
                    serviceRepo
                        .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                            Services.Where(s => ids.Contains(s.Id)).ToList());

                    var orderRepo = new Mock<IRepairOrderRepository>();
                    orderRepo
                        .Setup(r => r.AddAsync(It.IsAny<RepairOrder>(), It.IsAny<CancellationToken>()))
                        .Callback((RepairOrder o, CancellationToken _) => CreatedOrder = o)
                        .Returns(Task.CompletedTask);
                    orderRepo
                        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(1);

                    var authCodeRepo = new Mock<IAuthCodeRepository>();
                    authCodeRepo
                        .Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((string email, CancellationToken _) => AuthCodes.GetValueOrDefault(email));
                    authCodeRepo
                        .Setup(r => r.AddAsync(It.IsAny<AuthCode>(), It.IsAny<CancellationToken>()))
                        .Callback((AuthCode code, CancellationToken _) => AuthCodes[code.Email] = code)
                        .Returns(Task.CompletedTask);
                    authCodeRepo
                        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(1);

                    var notifier = new Mock<INotifier>();
                    notifier
                        .Setup(n => n.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Callback((string target, string text, CancellationToken _) => Notifications.Add((target, text)))
                        .Returns(Task.CompletedTask);

                    services.RemoveAll<IRepository<Device>>();
                    services.AddScoped(_ => deviceRepo.Object);
                    services.RemoveAll<IServiceRepository>();
                    services.AddScoped(_ => serviceRepo.Object);
                    services.RemoveAll<IRepairOrderRepository>();
                    services.AddScoped(_ => orderRepo.Object);
                    services.RemoveAll<IAuthCodeRepository>();
                    services.AddScoped(_ => authCodeRepo.Object);
                    services.RemoveAll<INotifier>();
                    services.AddScoped(_ => notifier.Object);

                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(_nowUtc));
                });
            });
        }
    }

    private static async Task<Dictionary<string, JsonElement>> ReadErrorsAsync(HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var errors = new Dictionary<string, JsonElement>();
        if (doc is not null && doc.RootElement.TryGetProperty("errors", out var errorsNode))
        {
            foreach (var property in errorsNode.EnumerateObject())
            {
                errors[property.Name] = property.Value;
            }
        }

        return errors;
    }

    private static object ValidBody(Guid[] serviceIds, string? email = "ivan@example.com") => new
    {
        name = "Иван",
        phone = "+79001234567",
        email,
        deviceType = "Phone",
        deviceManufacturer = "Apple",
        deviceModel = "iPhone 13",
        preferredVisitAt = "2026-10-07T12:00:00+04:00",
        serviceIds,
    };

    [Fact]
    public async Task Create_GuestWithEmail_CreatesPendingOrderAndSendsCode()
    {
        var harness = new CreateOrderHarness();
        using var factory = harness.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            name = "Иван",
            phone = "+79001234567",
            email = "  Ivan@Example.COM ",
            deviceType = "Phone",
            deviceManufacturer = "Apple",
            deviceModel = "iPhone 13",
            preferredVisitAt = "2026-10-07T12:00:00+04:00",
            serviceIds = Array.Empty<Guid>(),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(_jsonOptions);
        Assert.NotNull(result);
        Assert.True(result.RequiresEmailConfirmation);

        Assert.NotNull(harness.CreatedOrder);
        Assert.Equal(RepairStatus.PendingContact, harness.CreatedOrder.Status);
        Assert.Null(harness.CreatedOrder.ClientId);
        Assert.Equal("ivan@example.com", harness.CreatedOrder.ContactEmail);
        Assert.Equal("+79001234567", harness.CreatedOrder.ContactPhone);

        // Код ушёл на email заявки и совпадает с сохранённым
        var (target, text) = Assert.Single(harness.Notifications);
        Assert.Equal("ivan@example.com", target);
        var match = Regex.Match(text, @"\b\d{6}\b");
        Assert.True(match.Success);
        Assert.Equal(match.Value, harness.AuthCodes["ivan@example.com"].Code);
    }

    [Fact]
    public async Task Create_GuestWithoutEmail_Returns400WithEmailError()
    {
        var harness = new CreateOrderHarness();
        using var factory = harness.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", ValidBody([], email: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadErrorsAsync(response);
        Assert.True(errors.ContainsKey("Email"));
        Assert.Null(harness.CreatedOrder);
        Assert.Empty(harness.Notifications);
    }

    [Fact]
    public async Task Create_InvalidEmail_Returns400WithEmailError()
    {
        var harness = new CreateOrderHarness();
        using var factory = harness.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", ValidBody([], email: "не почта"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(harness.CreatedOrder);
    }

    [Fact]
    public async Task Create_AuthenticatedClient_CreatesActiveOrderWithoutCode()
    {
        var clientId = Guid.NewGuid();
        var harness = new CreateOrderHarness();
        using var factory = harness.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(clientId));

        var response = await client.PostAsJsonAsync("/api/orders", ValidBody([]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(_jsonOptions);
        Assert.False(result!.RequiresEmailConfirmation);

        Assert.NotNull(harness.CreatedOrder);
        Assert.Equal(RepairStatus.Created, harness.CreatedOrder.Status);
        Assert.Equal(clientId, harness.CreatedOrder.ClientId);
        Assert.Empty(harness.Notifications);
    }

    [Fact]
    public async Task Create_ValidRequest_Returns201WithOrderNumberAndSnapshotPrice()
    {
        var charging = new Service { Id = Guid.NewGuid(), Name = "Замена разъема зарядки", Price = 1200m, IsActive = true };
        var battery = new Service { Id = Guid.NewGuid(), Name = "Замена аккумулятора", Price = 1500m, IsActive = true };
        var harness = new CreateOrderHarness { Services = [charging, battery] };
        using var factory = harness.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", ValidBody([charging.Id, battery.Id]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(_jsonOptions);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);

        Assert.NotNull(harness.CreatedOrder);
        Assert.Equal(2700m, harness.CreatedOrder.TotalPrice);
        Assert.Equal(2, harness.CreatedOrder.Services.Count);

        Assert.NotNull(harness.CreatedDevice);
        Assert.Equal("Apple", harness.CreatedDevice.Manufacturer);
        Assert.Equal("iPhone 13", harness.CreatedDevice.Model);
    }

    [Fact]
    public async Task Create_WithProblemDescription_SavesTrimmedDescription()
    {
        var harness = new CreateOrderHarness();
        using var factory = harness.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            name = "Иван",
            phone = "+79001234567",
            email = "ivan@example.com",
            deviceType = "Phone",
            deviceManufacturer = "Apple",
            deviceModel = "iPhone 13",
            preferredVisitAt = "2026-10-07T12:00:00+04:00",
            serviceIds = Array.Empty<Guid>(),
            problemDescription = "  Разбит экран, не реагирует на касания  ",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(harness.CreatedOrder);
        Assert.Equal("Разбит экран, не реагирует на касания", harness.CreatedOrder.ProblemDescription);
    }

    [Fact]
    public async Task Create_ProblemDescriptionTooLong_Returns400()
    {
        var harness = new CreateOrderHarness();
        using var factory = harness.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            name = "Иван",
            phone = "+79001234567",
            email = "ivan@example.com",
            deviceType = "Phone",
            deviceManufacturer = "Apple",
            deviceModel = "iPhone 13",
            preferredVisitAt = "2026-10-07T12:00:00+04:00",
            serviceIds = Array.Empty<Guid>(),
            problemDescription = new string('я', 2001),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(harness.CreatedOrder);
    }

    [Fact]
    public async Task Create_InvalidPhone_Returns400WithPhoneError()
    {
        using var factory = new CreateOrderHarness().CreateFactory();
        using var client = factory.CreateClient();

        var body = new
        {
            name = "Иван",
            phone = "12345",
            email = "ivan@example.com",
            deviceType = "Phone",
            deviceManufacturer = "Apple",
            deviceModel = "iPhone 13",
            preferredVisitAt = "2026-10-07T12:00:00+04:00",
        };

        var response = await client.PostAsJsonAsync("/api/orders", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadErrorsAsync(response);
        Assert.True(errors.ContainsKey("Phone"));
    }

    [Fact]
    public async Task Create_PastVisitDate_Returns400WithVisitError()
    {
        using var factory = new CreateOrderHarness().CreateFactory();
        using var client = factory.CreateClient();

        var body = new
        {
            name = "Иван",
            phone = "+79001234567",
            email = "ivan@example.com",
            deviceType = "Phone",
            deviceManufacturer = "Apple",
            deviceModel = "iPhone 13",
            preferredVisitAt = "2026-10-04T12:00:00+04:00",
        };

        var response = await client.PostAsJsonAsync("/api/orders", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadErrorsAsync(response);
        Assert.True(errors.ContainsKey("PreferredVisitAt"));
    }

    [Fact]
    public async Task Create_UnknownService_Returns400WithServiceError()
    {
        using var factory = new CreateOrderHarness().CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", ValidBody([Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadErrorsAsync(response);
        Assert.True(errors.ContainsKey("ServiceIds"));
    }
}
