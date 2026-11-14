using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;
using ProPhoneService.Domain.Shared;

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
        Converters = { new JsonStringEnumConverter() },
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

        public bool FailNotification { get; init; }

        public RepairOrder? CreatedOrder { get; private set; }

        public Device? CreatedDevice { get; private set; }

        /// <summary>
        /// Коды, отправленные на email (хранилище одноразовых кодов)
        /// </summary>
        public Dictionary<string, AuthCode> AuthCodes { get; } = [];

        public List<(string Target, string Subject, string Text)> Notifications { get; } = [];

        public WebApplicationFactory<Program> CreateFactory()
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseSetting("Jwt:Key", TestJwt.Key);
                host.UseSetting("Database:MigrateOnStartup", "false");
                host.UseSetting("Notifications:RetryDelay", "00:00:00");
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
                        .Callback((RepairOrder o, CancellationToken _) => { CreatedOrder = o; o.CreatedAt = _nowUtc.UtcDateTime; })
                        .Returns(Task.CompletedTask);
                    orderRepo
                        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(1);

                    var authCodeRepo = new Mock<IAuthCodeRepository>();
                    authCodeRepo
                        .Setup(r => r.ExecuteForPhoneAsync(It.IsAny<string>(), It.IsAny<Func<Task<AuthOutcome>>>(), It.IsAny<CancellationToken>()))
                        .Returns((string _, Func<Task<AuthOutcome>> operation, CancellationToken _) => operation());
                    authCodeRepo
                        .Setup(r => r.GetByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((string email, CancellationToken _) => AuthCodes.GetValueOrDefault(email));
                    authCodeRepo
                        .Setup(r => r.AddAsync(It.IsAny<AuthCode>(), It.IsAny<CancellationToken>()))
                        .Callback((AuthCode code, CancellationToken _) => AuthCodes[code.Phone] = code)
                        .Returns(Task.CompletedTask);
                    authCodeRepo
                        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(1);

                    orderRepo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(() => CreatedOrder);
                    var bindingRepo = new Mock<IMessengerBindingRepository>();
                    bindingRepo.Setup(x => x.AddAsync(It.IsAny<MessengerBinding>(), It.IsAny<CancellationToken>()))
                        .Returns(Task.CompletedTask);
                    bindingRepo.Setup(x => x.GetByClientIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new MessengerBinding
                        {
                            Id = Guid.NewGuid(), TokenHash = "hash", Phone = "+79001234567", Channel = MessengerChannel.Vk,
                            AccountId = Guid.NewGuid(), RecipientId = "42", LinkedAt = _nowUtc.UtcDateTime,
                        });
                    services.RemoveAll<IRepository<NotificationLog>>();
                    services.AddSingleton(Mock.Of<IRepository<NotificationLog>>());
                    services.RemoveAll<IMessengerBindingRepository>();
                    services.AddSingleton(bindingRepo.Object);
                    services.RemoveAll<MessengerOptions>();
                    services.AddSingleton(MessengerDeliveryTests.Options());

                    var notifier = new Mock<INotifier>();
                    notifier
                        .Setup(n => n.SendAsync(
                            It.IsAny<MessengerChannel>(),
                            It.IsAny<string>(),
                            It.IsAny<string>(),
                            It.IsAny<CancellationToken>()))
                        .Callback((MessengerChannel channel, string target, string text, CancellationToken _) =>
                        {
                            if (FailNotification) throw new IOException("VK unavailable");
                            Notifications.Add((target, channel.ToString(), text));
                        })
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

    private static object ValidBody(Guid[] serviceIds, string? phone = "+79001234567") => new
    {
        name = "Иван",
        phone,
        deviceType = "Phone",
        deviceManufacturer = "Apple",
        deviceModel = "iPhone 13",
        preferredVisitAt = "2026-10-07T12:00:00+04:00",
        serviceIds,
    };

    [Fact]
    public async Task Create_GuestWithPhone_CreatesPendingOrderAndReturnsVkLink()
    {
        var harness = new CreateOrderHarness();
        using var factory = harness.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            name = "Иван",
            phone = "+79001234567",
            deviceType = "Phone",
            deviceManufacturer = "Apple",
            deviceModel = "iPhone 13",
            preferredVisitAt = "2026-10-07T12:00:00+04:00",
            serviceIds = Array.Empty<Guid>(),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(_jsonOptions);
        Assert.NotNull(result);
        Assert.True(result.RequiresContactConfirmation);
        Assert.Equal(harness.CreatedOrder!.OrderNumber, result.OrderNumber);

        Assert.NotNull(harness.CreatedOrder);
        Assert.Equal(RepairStatus.PendingContact, harness.CreatedOrder.Status);
        Assert.Null(harness.CreatedOrder.ClientId);
        Assert.Equal("Иван", harness.CreatedOrder.ContactName);
        Assert.Equal("+79001234567", harness.CreatedOrder.ContactPhone);

        Assert.Empty(harness.Notifications);
        Assert.NotNull(result.MessengerLink);
        Assert.StartsWith("https://vk.me/club123?ref=", result.MessengerLink.Url);
    }

    [Fact]
    public async Task Create_DoesNotSendCodeBeforeVkBinding()
    {
        var harness = new CreateOrderHarness { FailNotification = true };
        using var factory = harness.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", ValidBody([]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(RepairStatus.PendingContact, harness.CreatedOrder!.Status);
        Assert.Null(harness.CreatedOrder.ClientId);
    }

    [Fact]
    public async Task Create_GuestWithoutPhone_Returns400WithPhoneError()
    {
        var harness = new CreateOrderHarness();
        using var factory = harness.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", ValidBody([], phone: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadErrorsAsync(response);
        Assert.True(errors.ContainsKey("Phone"));
        Assert.Null(harness.CreatedOrder);
        Assert.Empty(harness.Notifications);
    }

    [Fact]
    public async Task Create_InvalidPhoneText_Returns400WithPhoneError()
    {
        var harness = new CreateOrderHarness();
        using var factory = harness.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", ValidBody([], phone: "не телефон"));

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
        Assert.False(result!.RequiresContactConfirmation);

        Assert.NotNull(harness.CreatedOrder);
        Assert.Equal(RepairStatus.Created, harness.CreatedOrder.Status);
        Assert.Equal(clientId, harness.CreatedOrder.ClientId);
        var notification = Assert.Single(harness.Notifications);
        Assert.Equal("42", notification.Target);
        Assert.Contains("Заявка принята", notification.Text);
        Assert.DoesNotContain("Ваш код подтверждения", notification.Text);
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

    [Fact]
    public async Task Create_AuthenticatedClientWithUnavailableVk_StillReturnsCreatedOrder()
    {
        var harness = new CreateOrderHarness { FailNotification = true };
        using var factory = harness.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(Guid.NewGuid()));

        var response = await client.PostAsJsonAsync("/api/orders", ValidBody([]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(RepairStatus.Created, harness.CreatedOrder!.Status);
        Assert.NotNull(harness.CreatedOrder.ClientId);
    }
}
