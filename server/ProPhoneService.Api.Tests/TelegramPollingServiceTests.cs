using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;
using ProPhoneService.Infrastructure.EfCore.Notifications;
using static ProPhoneService.Api.Tests.TelegramPollingClientTests;

namespace ProPhoneService.Api.Tests;

public class TelegramPollingServiceTests
{
    private const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static string Batch => """{"ok":true,"result":[{"update_id":10,"message":{"chat":{"type":"private","id":42},"from":{"id":42},"text":"/start TOKEN"}},{"update_id":11}]}"""
        .Replace("TOKEN", Token);

    [Fact]
    public async Task Polling_DeletesWebhookProcessesUpdatesAndAdvancesOffset()
    {
        var options = Options();
        var repository = Repository();
        var notifier = Notifier();
        using var provider = Provider(options, repository, notifier);
        var observed = new List<string>();
        var nextPoll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = Client(async (request, cancellation) =>
        {
            observed.Add(request.RequestUri!.AbsolutePath.Split('/').Last());

            if (observed[^1] == "deleteWebhook")
            {
                return Json("""{"ok":true,"result":true}""");
            }

            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));

            if (payload.RootElement.GetProperty("offset").GetInt64() == 12)
            {
                nextPoll.SetResult();
                await Task.Delay(Timeout.Infinite, cancellation);
            }

            return Json(Batch);
        });
        using var worker = Worker(provider, http, options);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await nextPoll.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(new[] { "deleteWebhook", "getUpdates", "getUpdates" }, observed);
        repository.Verify(x => x.TryBindAsync(It.IsAny<string>(), MessengerChannel.Telegram, "42",
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(x => x.SendAsync(MessengerChannel.Telegram, "42",
            It.Is<string>(text => text.Contains("Мессенджер успешно привязан!")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessingFailure_RetriesSameOffsetBeforeAcknowledging()
    {
        var options = Options();
        var repository = Repository();
        repository.SetupSequence(x => x.TryBindAsync(It.IsAny<string>(), MessengerChannel.Telegram, "42",
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("temporary database failure"))
            .ReturnsAsync(true);
        var notifier = Notifier();
        using var provider = Provider(options, repository, notifier);
        var offsets = new List<long>();
        var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = Client(async (request, cancellation) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("deleteWebhook"))
            {
                return Json("""{"ok":true,"result":true}""");
            }

            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            var offset = payload.RootElement.GetProperty("offset").GetInt64();
            offsets.Add(offset);

            if (offset == 12)
            {
                acknowledged.SetResult();
                await Task.Delay(Timeout.Infinite, cancellation);
            }

            return Json(Batch);
        });
        using var worker = Worker(provider, http, options);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(new long[] { 0, 0, 12 }, offsets);
        notifier.Verify(x => x.SendAsync(MessengerChannel.Telegram, "42", It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(TelegramUpdateMode.Webhook, true)]
    [InlineData(TelegramUpdateMode.Polling, false)]
    public async Task DisabledOrUnconfiguredPolling_DoesNotCallTelegram(TelegramUpdateMode mode, bool configured)
    {
        var options = Options();
        options.Telegram.UpdateMode = mode;

        if (!configured)
        {
            options.Telegram.Token = "";
        }

        using var provider = Provider(options, Repository(), Notifier());
        var calls = 0;
        using var http = Client((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Json("{}"));
        });
        using var worker = Worker(provider, http, options);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, calls);
    }

    private static MessengerOptions Options()
    {
        var options = MessengerDeliveryTests.Options();
        options.Telegram.UpdateMode = TelegramUpdateMode.Polling;
        options.Telegram.WebhookSecret = "";

        return options;
    }

    private static Mock<IMessengerBindingRepository> Repository()
    {
        var repository = new Mock<IMessengerBindingRepository>();
        repository.Setup(x => x.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessengerBinding
            {
                Id = Guid.NewGuid(),
                TokenHash = "hash",
                Phone = "+79001234567",
                AccountId = Guid.NewGuid(),
                Channel = MessengerChannel.Telegram,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            });
        repository.Setup(x => x.TryBindAsync(It.IsAny<string>(), MessengerChannel.Telegram, "42",
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        return repository;
    }

    private static Mock<INotifier> Notifier()
    {
        var notifier = new Mock<INotifier>();
        notifier.Setup(x => x.SendAsync(It.IsAny<MessengerChannel>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        return notifier;
    }

    private static ServiceProvider Provider(MessengerOptions options, Mock<IMessengerBindingRepository> repository,
        Mock<INotifier> notifier)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(_ => repository.Object);
        services.AddScoped(_ => Mock.Of<IRepairOrderRepository>());
        services.AddScoped(_ => Mock.Of<IAccountRepository>());
        services.AddScoped(_ => Mock.Of<IClientRepository>());
        services.AddSingleton(notifier.Object);
        services.AddScoped<MessengerBindingService>();
        services.AddScoped<ITelegramUpdateHandler, TelegramUpdateHandler>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static TelegramPollingService Worker(ServiceProvider provider, HttpClient http, MessengerOptions options)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("TelegramPolling")).Returns(http);

        return new TelegramPollingService(provider.GetRequiredService<IServiceScopeFactory>(), factory.Object,
            options, NullLogger<TelegramPollingService>.Instance);
    }
}
