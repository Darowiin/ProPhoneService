using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProPhoneService.Domain.Exceptions;
using ProPhoneService.Domain.Model;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

public class ChangeStatusEndpointTests
{
    private class OrderStore
    {
        public RepairOrder? Order { get; set; }

        public bool ThrowConcurrency { get; init; }

        /// <summary>
        /// Сколько первых вызовов провайдера завершатся ошибкой (проверка повторов)
        /// </summary>
        public int NotificationFailures { get; init; }

        public int SavedCount { get; private set; }

        public int NotificationCalls { get; private set; }

        public List<NotificationLog> Logs { get; } = [];

        public WebApplicationFactory<Program> CreateFactory()
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseSetting("Jwt:Key", TestJwt.Key);
                host.UseSetting("Database:MigrateOnStartup", "false");
                host.UseSetting("Notifications:RetryDelay", "00:00:00");
                host.ConfigureTestServices(services =>
                {
                    var repository = new Mock<IRepairOrderRepository>();
                    repository
                        .Setup(r => r.GetForStatusChangeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((Guid id, CancellationToken _) => Order is { Id: var orderId } && orderId == id
                            ? Order
                            : null);

                    repository
                        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .Callback(() => SavedCount++)
                        .ReturnsAsync(() =>
                        {
                            if (ThrowConcurrency)
                            {
                                throw new ConcurrencyConflictException("Изменён параллельно", new Exception());
                            }

                            return 1;
                        });

                    var notifier = new Mock<INotifier>();
                    notifier
                        .Setup(n => n.SendAsync(
                            It.IsAny<string>(),
                            It.IsAny<string>(),
                            It.IsAny<string>(),
                            It.IsAny<CancellationToken>()))
                        .Returns((string _, string _, string _, CancellationToken _) =>
                        {
                            NotificationCalls++;
                            return NotificationCalls <= NotificationFailures
                                ? Task.FromException(new InvalidOperationException("Провайдер недоступен"))
                                : Task.CompletedTask;
                        });

                    var notificationRepository = new Mock<IRepository<NotificationLog>>();
                    notificationRepository
                        .Setup(r => r.AddAsync(It.IsAny<NotificationLog>(), It.IsAny<CancellationToken>()))
                        .Callback((NotificationLog log, CancellationToken _) => Logs.Add(log))
                        .Returns(Task.CompletedTask);
                    notificationRepository
                        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(1);

                    services.RemoveAll<IRepairOrderRepository>();
                    services.AddScoped(_ => repository.Object);
                    services.RemoveAll<INotifier>();
                    services.AddScoped(_ => notifier.Object);
                    services.RemoveAll<IRepository<NotificationLog>>();
                    services.AddScoped(_ => notificationRepository.Object);
                });
            });
        }
    }

    private static RepairOrder NewOrder() =>
        RepairOrder.Create(Guid.NewGuid(), Guid.NewGuid(), preferredVisitAt: null);

    [Fact]
    public async Task ChangeStatus_ValidTransition_Returns204AndWritesHistory()
    {
        var order = NewOrder();
        var store = new OrderStore { Order = order };
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{order.Id}/status",
            new { status = "InDiagnostic", comment = "Взяли в диагностику" });

        Assert.Equal(RepairStatus.InDiagnostic, order.Status);
        var entry = Assert.Single(order.StatusHistory, h => h.Status == RepairStatus.InDiagnostic);
        Assert.Equal("Взяли в диагностику", entry.Comment);
        Assert.Equal(1, store.SavedCount);
    }

    [Fact]
    public async Task ChangeStatus_Success_SendsNotificationAndWritesSentLog()
    {
        var order = NewOrder();
        order.ContactEmail = "client@example.com";
        var store = new OrderStore { Order = order };
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{order.Id}/status",
            new { status = "InDiagnostic", comment = "Взяли в диагностику" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, store.NotificationCalls);

        var log = Assert.Single(store.Logs);
        Assert.Equal(order.Id, log.RepairOrderId);
        Assert.Equal("client@example.com", log.Recipient);
        Assert.Equal(NotificationDeliveryStatus.Sent, log.Status);
        Assert.Equal(1, log.Attempts);
        Assert.Contains("InDiagnostic", log.Message);
        Assert.Contains("Взяли в диагностику", log.Message);
        Assert.Equal($"Статус заказа: {RepairStatus.InDiagnostic}", log.Subject);
    }

    [Fact]
    public async Task ChangeStatus_ProviderUnavailable_RetriesThreeTimesAndKeepsStatusChange()
    {
        var order = NewOrder();
        order.ContactEmail = "client@example.com";
        var store = new OrderStore { Order = order, NotificationFailures = int.MaxValue };
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{order.Id}/status",
            new { status = "InDiagnostic", comment = (string?)null });

        // Сбой провайдера не ломает PATCH: смена статуса применена
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(RepairStatus.InDiagnostic, order.Status);
        Assert.Equal(3, store.NotificationCalls);

        var log = Assert.Single(store.Logs);
        Assert.Equal(NotificationDeliveryStatus.Failed, log.Status);
        Assert.Equal(3, log.Attempts);
    }

    [Fact]
    public async Task ChangeStatus_ProviderRecoversOnThirdAttempt_LogsSent()
    {
        var order = NewOrder();
        order.ContactEmail = "client@example.com";
        var store = new OrderStore { Order = order, NotificationFailures = 2 };
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{order.Id}/status",
            new { status = "InDiagnostic", comment = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(3, store.NotificationCalls);

        var log = Assert.Single(store.Logs);
        Assert.Equal(NotificationDeliveryStatus.Sent, log.Status);
        Assert.Equal(3, log.Attempts);
    }

    [Fact]
    public async Task ChangeStatus_NoRecipient_SkipsNotificationAndKeepsStatusChange()
    {
        var order = NewOrder();
        var store = new OrderStore { Order = order };
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{order.Id}/status",
            new { status = "InDiagnostic", comment = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, store.NotificationCalls);
        Assert.Empty(store.Logs);
    }

    [Fact]
    public async Task ChangeStatus_ForbiddenJump_Returns409()
    {
        var order = NewOrder(); // Created
        var store = new OrderStore { Order = order };
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{order.Id}/status",
            new { status = "Ready", comment = "Готово" }); // перепрыг Created -> Ready запрещён

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RepairStatus.Created, order.Status);
        Assert.Equal(0, store.SavedCount);
    }

    [Fact]
    public async Task ChangeStatus_BackwardTransition_Returns409()
    {
        var order = NewOrder();
        order.ChangeStatus(RepairStatus.InDiagnostic);
        var store = new OrderStore { Order = order };
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{order.Id}/status",
            new { status = "Created", comment = (string?)null }); // назад тоже нельзя

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RepairStatus.InDiagnostic, order.Status);
        Assert.Equal(0, store.SavedCount);
    }

    [Fact]
    public async Task ChangeStatus_UnknownOrder_Returns404()
    {
        var store = new OrderStore { Order = null };
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{Guid.NewGuid()}/status",
            new { status = "InDiagnostic", comment = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, store.SavedCount);
    }

    [Fact]
    public async Task ChangeStatus_TerminalStatus_Returns409()
    {
        var order = NewOrder();
        foreach (var next in new[] { RepairStatus.InDiagnostic, RepairStatus.Agreed, RepairStatus.InProgress })
        {
            order.ChangeStatus(next);
        }

        var store = new OrderStore { Order = order };
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{order.Id}/status",
            new { status = "Cancelled", comment = "Поздно" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        response = await client.PatchAsJsonAsync(
            $"/api/orders/{order.Id}/status",
            new { status = "InDiagnostic", comment = "Воскрешение" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RepairStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task ChangeStatus_ConcurrencyConflict_Returns409()
    {
        var order = NewOrder();
        var store = new OrderStore { Order = order, ThrowConcurrency = true };
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient().WithToken(TestJwt.ForMaster());

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{order.Id}/status",
            new { status = "InDiagnostic", comment = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
