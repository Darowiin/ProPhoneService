using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

/// <summary>
/// Проверка работы ролей Client/Master, защита панели мастера и запрет IDOR
/// </summary>
public class AuthorizationEndpointTests
{
    /// <summary>
    /// Заказы с клиентами и устройствами для проверки изоляции доступа
    /// </summary>
    private class OrderSource
    {
        public List<RepairOrder> Orders { get; init; } = [];

        public Dictionary<Guid, Client> Clients { get; init; } = [];

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
                        .ReturnsAsync((RepairStatus? _, DateTime? _, DateTime? _, int _, int _, CancellationToken _) =>
                            (IReadOnlyList<RepairOrder>)Orders);

                    repository
                        .Setup(r => r.GetWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((Guid id, CancellationToken _) => Fill(Orders.FirstOrDefault(o => o.Id == id)));

                    repository
                        .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((Guid id, CancellationToken _) => Orders.FirstOrDefault(o => o.Id == id));

                    repository
                        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(1);

                    services.RemoveAll<IRepairOrderRepository>();
                    services.AddScoped(_ => repository.Object);

                    var serviceRepository = new Mock<IServiceRepository>();
                    serviceRepository
                        .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(Array.Empty<Service>());

                    services.RemoveAll<IServiceRepository>();
                    services.AddScoped(_ => serviceRepository.Object);
                });
            });
        }

        private RepairOrder? Fill(RepairOrder? order)
        {
            if (order is null)
            {
                return null;
            }

            order.Client = Clients.GetValueOrDefault(order.ClientId);
            order.Device = new Device
            {
                Id = order.DeviceId,
                Type = DeviceType.Phone,
                Manufacturer = "Apple",
                Model = "iPhone 13",
            };

            return order;
        }
    }

    private static OrderSource SourceWithOrder(out RepairOrder order, out Guid clientId)
    {
        clientId = Guid.NewGuid();
        var client = new Client { Id = clientId, Name = "Иван", Email = "ivan@example.com" };
        order = RepairOrder.Create(clientId, Guid.NewGuid());

        return new OrderSource
        {
            Orders = [order],
            Clients = new Dictionary<Guid, Client> { [clientId] = client },
        };
    }

    [Fact]
    public async Task GetPage_Anonymous_Returns401()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPage_ClientToken_Returns403()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(Guid.NewGuid()));

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPage_ForgedSignature_Returns401()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForgedForMaster());

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPage_MasterToken_Returns200()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ForeignOrder_ClientToken_Returns404()
    {
        var source = SourceWithOrder(out var order, out _);
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(Guid.NewGuid()));

        var response = await client.GetAsync($"/api/orders/{order.Id}");

        // Чужой заказ неотличим от несуществующего - не раскрываем существование
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_OwnOrder_ClientToken_Returns200()
    {
        var source = SourceWithOrder(out var order, out var clientId);
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(clientId));

        var response = await client.GetAsync($"/api/orders/{order.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetById_AnyOrder_MasterToken_Returns200()
    {
        var source = SourceWithOrder(out var order, out _);
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.GetAsync($"/api/orders/{order.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetById_Anonymous_Returns401()
    {
        var source = SourceWithOrder(out var order, out var clientId);
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/orders/{order.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangeStatus_ClientToken_Returns403()
    {
        var source = SourceWithOrder(out var order, out var clientId);
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(clientId));

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{order.Id}/status",
            new { status = "InDiagnostic" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RepairStatus.Created, order.Status);
    }

    [Fact]
    public async Task CreateService_ClientToken_Returns403()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForClient(Guid.NewGuid()));

        var response = await client.PostAsJsonAsync("/api/services", new { name = "Замена стекла", price = 900m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetServices_Anonymous_Returns200()
    {
        using var factory = new OrderSource().CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/services");

        // Публичный прайс-лист для сайта
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
