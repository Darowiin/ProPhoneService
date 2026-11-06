using System.Reflection;
using ProPhoneService.Application.Services;

namespace ProPhoneService.Api.Tests;

public class RefreshTokenStoreTests
{
    [Fact]
    public async Task ConcurrentConsumption_OnlyOneRequestSucceeds()
    {
        using var store = new InMemoryRefreshTokenStore(TimeProvider.System);
        var account = Guid.NewGuid();
        var token = store.Issue(account, DateTimeOffset.UtcNow.AddDays(1));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = Enumerable.Range(0, 32).Select(async _ =>
        {
            await start.Task;
            return store.TryConsume(token);
        }).ToArray();

        start.SetResult();
        var results = await Task.WhenAll(requests);

        Assert.Single(results, result => result == account);
        Assert.Equal(31, results.Count(result => result is null));
    }

    [Fact]
    public void Cleanup_RemovesAbandonedExpiredTokensAndKeepsLiveSessions()
    {
        var time = new ManualTimeProvider();
        using var store = new InMemoryRefreshTokenStore(time);
        store.Issue(Guid.NewGuid(), time.Now.AddMinutes(1));
        var account = Guid.NewGuid();
        var live = store.Issue(account, time.Now.AddHours(1));

        time.Now = time.Now.AddMinutes(2);
        time.Tick();

        var entries = typeof(InMemoryRefreshTokenStore).GetField("_tokens", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(store)!;
        Assert.Equal(1, entries.GetType().GetProperty("Count")!.GetValue(entries));
        Assert.Equal(account, store.TryConsume(live));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        private Action? _tick;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Tick() => _tick!();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _tick = () => callback(state);
            return new ManualTimer();
        }
        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
