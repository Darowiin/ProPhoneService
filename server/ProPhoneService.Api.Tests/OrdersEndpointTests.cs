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

public class OrdersEndpointTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Заказ с устройством и историей, отсортированной как в БД
    /// </summary>
    private static (RepairOrder Order, Device Device) MakeOrderWithDevice(RepairStatus status, DateTime createdAt)
    {
        var order = RepairOrder.Create(Guid.NewGuid(), Guid.NewGuid(), null);
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

    /// <summary>
    /// Заказ с устройством и заданной желаемой датой визита (для фильтра по preferred_visit_at)
    /// </summary>
    private static (RepairOrder Order, Device Device) MakeOrderWithVisit(RepairStatus status, DateTime? preferredVisitAt)
    {
        var (order, device) = MakeOrderWithDevice(status, DateTime.UtcNow);

        typeof(RepairOrder).GetProperty(nameof(RepairOrder.PreferredVisitAt))!.SetValue(order, preferredVisitAt);

        return (order, device);
    }

    private class OrderSource
    {
        public List<RepairOrder> Orders { get; set; } = [];

        public Dictionary<Guid, Device> Devices { get; set; } = [];

        public Dictionary<Guid, Client> Clients { get; set; } = [];

        public WebApplicationFactory<Program> CreateFactory()
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseSetting("Jwt:Key", TestJwt.Key);
                host.ConfigureTestServices(services =>
                {
                    var repository = new Mock<IRepairOrderRepository>();
                    repository
                        .Setup(r => r.GetPageAsync(
                            It.IsAny<RepairStatus?>(),
                            It.IsAny<DateTime?>(),
                            It.IsAny<DateTime?>(),
                            It.IsAny<int>(),
                            It.IsAny<int>(),
                            It.IsAny<CancellationToken>()))
                        .ReturnsAsync((RepairStatus? status, DateTime? visitFrom, DateTime? visitTo, int skip, int take, CancellationToken _) =>
                        {
                            IEnumerable<RepairOrder> query = Orders
                                .Where(o => status == null || o.Status == status)
                                .Where(o => (visitFrom == null && visitTo == null) || (o.PreferredVisitAt != null
                                    && (visitFrom == null || o.PreferredVisitAt >= visitFrom)
                                    && (visitTo == null || o.PreferredVisitAt <= visitTo)))
                                .OrderByDescending(o => o.CreatedAt)
                                .ThenByDescending(o => o.Id);

                            var page = query.Skip(skip).Take(take).ToList();
                            foreach (var order in page)
                            {
                                order.Device = Devices.GetValueOrDefault(order.DeviceId);
                            }

                            return page;
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
                            order.Client = Clients.GetValueOrDefault(order.ClientId);

                            return order;
                        });

                    services.RemoveAll<IRepairOrderRepository>();
                    services.AddScoped(_ => repository.Object);
                });
            });
        }
    }

    [Fact]
    public async Task GetPage_WithoutFilters_Returns200WithSortedOrders()
    {
        var (oldOrder, oldDevice) = MakeOrderWithDevice(RepairStatus.Created, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc));
        var (newOrder, newDevice) = MakeOrderWithDevice(RepairStatus.InProgress, new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc));
        var source = new OrderSource
        {
            Orders = [oldOrder, newOrder],
            Devices = new Dictionary<Guid, Device> { [oldOrder.DeviceId] = oldDevice, [newOrder.DeviceId] = newDevice },
        };
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<RepairOrderListItemDto>>(_jsonOptions);
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
        // Новые сверху
        Assert.Equal(newOrder.Id, items[0].Id);
        Assert.Equal(RepairStatus.InProgress, items[0].Status);
        Assert.Equal("iPhone 13", items[0].Device.Model);
        Assert.Equal("Apple", items[0].Device.Manufacturer);
        Assert.Equal("Phone", items[0].Device.Type);
        // Тайм-лайн: Created -> InDiagnostic -> Agreed -> InProgress
        Assert.NotEmpty(items[0].StatusTimeline);
        Assert.Equal(RepairStatus.Created, items[0].StatusTimeline[0].Status);
        Assert.Equal(RepairStatus.InProgress, items[0].StatusTimeline[^1].Status);
    }

    [Fact]
    public async Task GetPage_WithStatusFilter_Returns200OnlyMatching()
    {
        var (createdOrder, createdDevice) = MakeOrderWithDevice(RepairStatus.Created, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc));
        var (inProgressOrder, inProgressDevice) = MakeOrderWithDevice(RepairStatus.InProgress, new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc));
        var source = new OrderSource
        {
            Orders = [createdOrder, inProgressOrder],
            Devices = new Dictionary<Guid, Device> { [createdOrder.DeviceId] = createdDevice, [inProgressOrder.DeviceId] = inProgressDevice },
        };
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync("/api/orders?status=InProgress");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<RepairOrderListItemDto>>(_jsonOptions);
        Assert.NotNull(items);
        var item = Assert.Single(items);
        Assert.Equal(inProgressOrder.Id, item.Id);
    }

    [Fact]
    public async Task GetPage_UnknownStatus_Returns400()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync("/api/orders?status=NotAStatus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPage_InvalidPage_Returns400()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync("/api/orders?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPage_PageBeyondIntOverflow_Returns400()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        // Граница переполнения (page-1)*25 > int.MaxValue: page 85 899 347 даёт отрицательный skip
        var response = await client.GetAsync("/api/orders?page=85899347");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ExistingOrder_Returns200WithFullCard()
    {
        var clientEntity = new Client { Id = Guid.NewGuid(), Name = "Иван", Phone = "+79001234567", Email = "ivan@example.com" };
        var device = new Device { Id = Guid.NewGuid(), Type = DeviceType.Phone, Manufacturer = "Apple", Model = "iPhone 13" };
        var order = RepairOrder.Create(clientEntity.Id, device.Id, new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc));
        while (order.Status != RepairStatus.InProgress)
        {
            order.ChangeStatus(RepairStatusTransitions.GetAllowed(order.Status!.Value).First());
        }

        order.TotalPrice = 2700m;
        order.ProblemDescription = "Не заряжается и греется";
        var svcCharging = new Service { Id = Guid.NewGuid(), Name = "Замена разъема зарядки", Price = 1200m, IsActive = true };
        var svcBattery = new Service { Id = Guid.NewGuid(), Name = "Замена аккумулятора", Price = 1500m, IsActive = true };
        order.Services.Add(new RepairOrderService { RepairOrderId = order.Id, ServiceId = svcCharging.Id, Price = 1200m, Service = svcCharging });
        order.Services.Add(new RepairOrderService { RepairOrderId = order.Id, ServiceId = svcBattery.Id, Price = 1500m, Service = svcBattery });

        var source = new OrderSource
        {
            Orders = [order],
            Devices = new Dictionary<Guid, Device> { [order.DeviceId] = device },
            Clients = new Dictionary<Guid, Client> { [order.ClientId] = clientEntity },
        };
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync($"/api/orders/{order.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var card = await response.Content.ReadFromJsonAsync<RepairOrderDetailDto>(_jsonOptions);
        Assert.NotNull(card);
        Assert.Equal(order.Id, card.Id);
        Assert.Equal(RepairStatus.InProgress, card.Status);
        Assert.Equal("iPhone 13", card.Device.Model);

        // Клиент из карточки
        Assert.Equal("Иван", card.Client.Name);
        Assert.Equal("+79001234567", card.Client.Phone);
        Assert.Equal("ivan@example.com", card.Client.Email);

        // Услуги с зафиксированными ценами
        Assert.Equal(2, card.Services.Count);
        Assert.Contains(card.Services, s => s.Name == "Замена разъема зарядки" && s.Price == 1200m);
        Assert.Contains(card.Services, s => s.Name == "Замена аккумулятора" && s.Price == 1500m);
        Assert.Equal(2700m, card.TotalPrice);
        Assert.Equal("Не заряжается и греется", card.ProblemDescription);

        // Тайм-лайн хронологичен
        Assert.Equal(RepairStatus.Created, card.StatusTimeline[0].Status);
        Assert.Equal(RepairStatus.InProgress, card.StatusTimeline[^1].Status);
    }

    [Fact]
    public async Task GetById_UnknownOrder_Returns404()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPage_VisitDateRange_Returns200WithOnlyOrdersInRange()
    {
        var (oldOrder, oldDevice) = MakeOrderWithVisit(RepairStatus.Created, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc));
        var (newOrder, newDevice) = MakeOrderWithVisit(RepairStatus.Agreed, new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc));
        var source = new OrderSource
        {
            Orders = [oldOrder, newOrder],
            Devices = new Dictionary<Guid, Device> { [oldOrder.DeviceId] = oldDevice, [newOrder.DeviceId] = newDevice },
        };
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync("/api/orders?visitFrom=2026-10-01T00:00:00Z&visitTo=2026-10-05T23:59:59Z");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<RepairOrderListItemDto>>(_jsonOptions);
        Assert.NotNull(items);
        var item = Assert.Single(items);
        Assert.Equal(oldOrder.Id, item.Id);
    }

    [Fact]
    public async Task GetPage_VisitDateWithoutRange_Returns200ForNullVisit()
    {
        // Заказ без желаемой даты визита не должен пропадать, если фильтр по датам не задан
        var (order, device) = MakeOrderWithVisit(RepairStatus.Created, null);
        var source = new OrderSource
        {
            Orders = [order],
            Devices = new Dictionary<Guid, Device> { [order.DeviceId] = device },
        };
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<RepairOrderListItemDto>>(_jsonOptions);
        Assert.NotNull(items);
        Assert.Single(items);
    }

    [Fact]
    public async Task GetPage_VisitDateEmptyRange_Returns200WithEmptyArray()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync("/api/orders?visitFrom=2026-11-01T00:00:00Z&visitTo=2026-11-02T23:59:59Z");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<RepairOrderListItemDto>>(_jsonOptions);
        Assert.NotNull(items);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetPage_VisitFromLaterThanVisitTo_Returns400()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync("/api/orders?visitFrom=2026-10-05T00:00:00Z&visitTo=2026-10-01T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPage_EmptyResult_Returns200WithEmptyArray()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<RepairOrderListItemDto>>(_jsonOptions);
        Assert.NotNull(items);
        Assert.Empty(items);
    }
}
