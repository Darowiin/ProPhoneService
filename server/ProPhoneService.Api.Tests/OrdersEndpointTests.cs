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

    private class OrderSource
    {
        public List<RepairOrder> Orders { get; set; } = [];

        public Dictionary<Guid, Device> Devices { get; set; } = [];

        public WebApplicationFactory<Program> CreateFactory()
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.ConfigureTestServices(services =>
                {
                    var repository = new Mock<IRepairOrderRepository>();
                    repository
                        .Setup(r => r.GetPageAsync(
                            It.IsAny<RepairStatus?>(),
                            It.IsAny<int>(),
                            It.IsAny<int>(),
                            It.IsAny<CancellationToken>()))
                        .ReturnsAsync((RepairStatus? status, int skip, int take, CancellationToken _) =>
                        {
                            IEnumerable<RepairOrder> query = Orders
                                .Where(o => status == null || o.Status == status)
                                .OrderByDescending(o => o.CreatedAt)
                                .ThenByDescending(o => o.Id);

                            var page = query.Skip(skip).Take(take).ToList();
                            foreach (var order in page)
                            {
                                order.Device = Devices.GetValueOrDefault(order.DeviceId);
                            }

                            return page;
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
        using var client = factory.CreateClient();

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
        using var client = factory.CreateClient();

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
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders?status=NotAStatus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPage_InvalidPage_Returns400()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPage_EmptyResult_Returns200WithEmptyArray()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<RepairOrderListItemDto>>(_jsonOptions);
        Assert.NotNull(items);
        Assert.Empty(items);
    }
}
