using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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

/// <summary>
/// Проверка работы личного кабинета - только свои заказы, карточка с тайм-лайном
/// </summary>
public class MeOrdersEndpointTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private class MyOrdersSource
    {
        public List<RepairOrder> Orders { get; init; } = [];

        public Dictionary<Guid, Device> Devices { get; init; } = [];

        public Dictionary<Guid, Client> Clients { get; init; } = [];

        public WebApplicationFactory<Program> CreateFactory()
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseSetting("Jwt:Key", TestJwt.Key);
                host.UseSetting("Database:MigrateOnStartup", "false");
                host.ConfigureTestServices(services =>
                {
                    var repository = new Mock<IRepairOrderRepository>();
                    repository
                        .Setup(r => r.GetPageByClientIdAsync(
                            It.IsAny<Guid>(),
                            It.IsAny<RepairStatus?>(),
                            It.IsAny<int>(),
                            It.IsAny<int>(),
                            It.IsAny<CancellationToken>()))
                        .ReturnsAsync((Guid clientId, RepairStatus? status, int skip, int take, CancellationToken _) =>
                        {
                            // Повторяет фильтрацию репозитория: свои, без PendingContact, фильтр по статусу
                            var page = Orders
                                .Where(o => o.ClientId == clientId)
                                .Where(o => o.Status != RepairStatus.PendingContact)
                                .Where(o => status == null || o.Status == status)
                                .OrderByDescending(o => o.CreatedAt)
                                .ThenByDescending(o => o.Id)
                                .Skip(skip)
                                .Take(take)
                                .ToList();

                            foreach (var order in page)
                            {
                                order.Device = Devices.GetValueOrDefault(order.DeviceId);
                            }

                            return (IReadOnlyList<RepairOrder>)page;
                        });

                    repository
                        .Setup(r => r.GetWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((Guid id, CancellationToken _) =>
                        {
                            var order = Orders.FirstOrDefault(o => o.Id == id);
                            if (order is null)
                            {
                                return null;
                            }

                            order.Device = Devices.GetValueOrDefault(order.DeviceId);
                            if (order.ClientId is Guid orderClientId)
                            {
                                order.Client = Clients.GetValueOrDefault(orderClientId);
                            }

                            return order;
                        });

                    services.RemoveAll<IRepairOrderRepository>();
                    services.AddScoped(_ => repository.Object);
                });
            });
        }
    }

    private static (RepairOrder Order, Device Device) MakeOrder(Guid clientId, RepairStatus status, DateTime createdAt)
    {
        var order = RepairOrder.Create(clientId, Guid.NewGuid(), null);
        while (order.Status != status)
        {
            order.ChangeStatus(RepairStatusTransitions.GetAllowed(order.Status!.Value).First());
        }

        typeof(RepairOrder).GetProperty(nameof(RepairOrder.CreatedAt))!.SetValue(order, createdAt);

        var device = new Device
        {
            Id = order.DeviceId,
            Type = DeviceType.Phone,
            Manufacturer = "Apple",
            Model = "iPhone 13",
        };

        return (order, device);
    }

    [Fact]
    public async Task GetOrders_Anonymous_Returns401()
    {
        using var factory = new MyOrdersSource().CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/me/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrders_ReturnsOnlyOwnOrders()
    {
        var myClientId = Guid.NewGuid();
        var (myOrder, myDevice) = MakeOrder(myClientId, RepairStatus.InProgress, new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc));
        var (foreignOrder, foreignDevice) = MakeOrder(Guid.NewGuid(), RepairStatus.Created, new DateTime(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc));

        var source = new MyOrdersSource
        {
            Orders = [myOrder, foreignOrder],
            Devices = new Dictionary<Guid, Device>
            {
                [myOrder.DeviceId] = myDevice,
                [foreignOrder.DeviceId] = foreignDevice,
            },
        };
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(myClientId));

        var response = await client.GetAsync("/api/me/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<RepairOrderListItemDto>>(_jsonOptions);
        var item = Assert.Single(items!);
        Assert.Equal(myOrder.Id, item.Id);
        Assert.Equal(RepairStatus.InProgress, item.Status);
        Assert.Equal("iPhone 13", item.Device.Model);
    }

    [Fact]
    public async Task GetOrders_StatusFilter_ReturnsMatchingOnly()
    {
        var myClientId = Guid.NewGuid();
        var (createdOrder, createdDevice) = MakeOrder(myClientId, RepairStatus.Created, new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc));
        var (readyOrder, readyDevice) = MakeOrder(myClientId, RepairStatus.Ready, new DateTime(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc));

        var source = new MyOrdersSource
        {
            Orders = [createdOrder, readyOrder],
            Devices = new Dictionary<Guid, Device>
            {
                [createdOrder.DeviceId] = createdDevice,
                [readyOrder.DeviceId] = readyDevice,
            },
        };
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(myClientId));

        var response = await client.GetAsync("/api/me/orders?status=Ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<RepairOrderListItemDto>>(_jsonOptions))!);
        Assert.Equal(readyOrder.Id, item.Id);
    }

    [Fact]
    public async Task GetOrders_InvalidPage_Returns400()
    {
        using var factory = new MyOrdersSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(Guid.NewGuid()));

        var response = await client.GetAsync("/api/me/orders?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_OwnOrder_Returns200WithFullCard()
    {
        var clientEntity = new Client { Id = Guid.NewGuid(), Name = "Иван", Email = "ivan@example.com" };
        var (order, device) = MakeOrder(clientEntity.Id, RepairStatus.InProgress, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc));
        order.TotalPrice = 2700m;

        var charging = new Service { Id = Guid.NewGuid(), Name = "Замена разъема зарядки", Price = 1200m, IsActive = true };
        var battery = new Service { Id = Guid.NewGuid(), Name = "Замена аккумулятора", Price = 1500m, IsActive = true };
        order.Services.Add(new RepairOrderService { RepairOrderId = order.Id, ServiceId = charging.Id, Price = 1200m, Service = charging });
        order.Services.Add(new RepairOrderService { RepairOrderId = order.Id, ServiceId = battery.Id, Price = 1500m, Service = battery });

        var source = new MyOrdersSource
        {
            Orders = [order],
            Devices = new Dictionary<Guid, Device> { [order.DeviceId] = device },
            Clients = new Dictionary<Guid, Client> { [clientEntity.Id] = clientEntity },
        };
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(clientEntity.Id));

        var response = await client.GetAsync($"/api/me/orders/{order.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var card = await response.Content.ReadFromJsonAsync<RepairOrderDetailDto>(_jsonOptions);
        Assert.NotNull(card);
        Assert.Equal(order.Id, card.Id);
        Assert.Equal("iPhone 13", card.Device.Model);
        Assert.Equal(2700m, card.TotalPrice);
        Assert.Equal(2, card.Services.Count);
        Assert.Contains(card.Services, s => s.Name == "Замена аккумулятора" && s.Price == 1500m);
        Assert.Equal(RepairStatus.Created, card.StatusTimeline[0].Status);
        Assert.Equal(RepairStatus.InProgress, card.StatusTimeline[^1].Status);
    }

    [Fact]
    public async Task GetById_ForeignOrder_Returns404()
    {
        var (order, device) = MakeOrder(Guid.NewGuid(), RepairStatus.Created, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc));
        var source = new MyOrdersSource
        {
            Orders = [order],
            Devices = new Dictionary<Guid, Device> { [order.DeviceId] = device },
        };
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(Guid.NewGuid()));

        var response = await client.GetAsync($"/api/me/orders/{order.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_PendingOrder_Returns404()
    {
        var clientId = Guid.NewGuid();
        var pending = RepairOrder.CreatePending(Guid.NewGuid(), "guest@example.com", null);
        var source = new MyOrdersSource { Orders = [pending] };
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(clientId));

        var response = await client.GetAsync($"/api/me/orders/{pending.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_Anonymous_Returns401()
    {
        var (order, device) = MakeOrder(Guid.NewGuid(), RepairStatus.Created, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc));
        var source = new MyOrdersSource
        {
            Orders = [order],
            Devices = new Dictionary<Guid, Device> { [order.DeviceId] = device },
        };
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/me/orders/{order.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
