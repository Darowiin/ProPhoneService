using ProPhoneService.Domain.Exceptions;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Tests;

public class RepairStatusTransitionTests
{
    private static RepairOrder CreateStartedOrder()
    {
        return RepairOrder.Create(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1));
    }

    public static readonly TheoryData<RepairStatus, RepairStatus> AllowedTransitions = new()
    {
        { RepairStatus.Created, RepairStatus.InProgress },
        { RepairStatus.Created, RepairStatus.Ready },
        { RepairStatus.Created, RepairStatus.Cancelled },
        { RepairStatus.InProgress, RepairStatus.Ready },
        { RepairStatus.InProgress, RepairStatus.Cancelled },
        { RepairStatus.Ready, RepairStatus.Completed },
        { RepairStatus.PendingContact, RepairStatus.Created },
        { RepairStatus.PendingContact, RepairStatus.Cancelled },
    };

    public static readonly TheoryData<RepairStatus, RepairStatus> ForbiddenTransitions = new()
    {
        { RepairStatus.PendingContact, RepairStatus.InProgress },
        { RepairStatus.Created, RepairStatus.PendingContact },
        { RepairStatus.Created, RepairStatus.Completed },
        { RepairStatus.InProgress, RepairStatus.Completed },
        { RepairStatus.InProgress, RepairStatus.Created },
        { RepairStatus.Ready, RepairStatus.InProgress },
        { RepairStatus.Completed, RepairStatus.Ready },
        { RepairStatus.Ready, RepairStatus.Cancelled },
        { RepairStatus.Created, RepairStatus.Created },
        { RepairStatus.Ready, RepairStatus.Ready },
        { RepairStatus.Completed, RepairStatus.Created },
        { RepairStatus.Completed, RepairStatus.Cancelled },
        { RepairStatus.Cancelled, RepairStatus.Created },
        { RepairStatus.Cancelled, RepairStatus.InProgress },
    };

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public void ChangeStatus_AllowedTransition_UpdatesStatusAndWritesHistory(RepairStatus from, RepairStatus to)
    {
        var order = CreateOrderInStatus(from);

        order.ChangeStatus(to);

        Assert.Equal(to, order.Status);
        var lastEntry = Assert.Single(order.StatusHistory, x => x.Status == to);
        Assert.Equal(order.Id, lastEntry.RepairOrderId);
    }

    [Theory]
    [MemberData(nameof(ForbiddenTransitions))]
    public void ChangeStatus_ForbiddenTransition_Throws(RepairStatus from, RepairStatus to)
    {
        var order = CreateOrderInStatus(from);
        var historyCountBefore = order.StatusHistory.Count;

        var exception = Assert.Throws<InvalidStatusTransitionException>(() => order.ChangeStatus(to));

        Assert.Contains(from.ToString(), exception.Message);
        Assert.Contains(to.ToString(), exception.Message);
        Assert.Equal(from, order.Status);
        Assert.Equal(historyCountBefore, order.StatusHistory.Count);
    }

    [Fact]
    public void Start_WritesInitialHistoryEntry()
    {
        var order = new RepairOrder { Id = Guid.NewGuid(), ClientId = Guid.NewGuid(), DeviceId = Guid.NewGuid(), PreferredVisitAt = DateTime.UtcNow.AddDays(1) };

        order.Start();

        Assert.Equal(RepairStatus.Created, order.Status);
        var entry = Assert.Single(order.StatusHistory);
        Assert.Equal(RepairStatus.Created, entry.Status);
    }

    [Fact]
    public void Start_AlreadyStarted_Throws()
    {
        var order = CreateStartedOrder();

        Assert.Throws<InvalidOperationException>(order.Start);
    }

    [Fact]
    public void ChangeStatus_BeforeStart_Throws()
    {
        var order = new RepairOrder { Id = Guid.NewGuid(), ClientId = Guid.NewGuid(), DeviceId = Guid.NewGuid() };

        Assert.Throws<InvalidStatusTransitionException>(() => order.ChangeStatus(RepairStatus.InProgress));

        Assert.Null(order.Status);
        Assert.Empty(order.StatusHistory);
    }

    [Fact]
    public void Start_DoesNotDependOnLoadedHistory_ThrowsOnPersistedOrder()
    {
        // Симуляция загрузки без Include(StatusHistory): история не загружена, но Status уже установлен в БД
        var order = CreateStartedOrder();
        order.StatusHistory.Clear();

        Assert.Throws<InvalidOperationException>(order.Start);
    }

    [Fact]
    public void GetAllowed_ReturnsImmutableWrapper_ExternalMutationCannotChangeRules()
    {
        var allowed = RepairStatusTransitions.GetAllowed(RepairStatus.Created);

        if (allowed is RepairStatus[] mutable)
        {
            mutable[0] = RepairStatus.Completed;
        }

        Assert.Equal(
            new[] { RepairStatus.InProgress, RepairStatus.Ready, RepairStatus.Cancelled },
            RepairStatusTransitions.GetAllowed(RepairStatus.Created));
    }

    [Fact]
    public void ChangeStatus_Cancelled_KeepsCommentInHistory()
    {
        var order = CreateOrderInStatus(RepairStatus.InProgress);

        order.ChangeStatus(RepairStatus.Cancelled, comment: "Клиент передумал");

        var lastEntry = Assert.Single(order.StatusHistory, x => x.Status == RepairStatus.Cancelled);
        Assert.Equal("Клиент передумал", lastEntry.Comment);
    }

    /// <summary>
    /// Создать заказ и довести его по цепочке до нужного статуса
    /// </summary>
    private static RepairOrder CreateOrderInStatus(RepairStatus target)
    {
        if (target == RepairStatus.PendingContact)
        {
            return RepairOrder.CreatePending(Guid.NewGuid(), null, DateTime.UtcNow.AddDays(1));
        }

        var order = CreateStartedOrder();

        var path = new Dictionary<RepairStatus, RepairStatus[]>
        {
            [RepairStatus.Created] = [],
            [RepairStatus.InProgress] = [RepairStatus.InProgress],
            [RepairStatus.Ready] = [RepairStatus.InProgress, RepairStatus.Ready],
            [RepairStatus.Completed] = [RepairStatus.InProgress, RepairStatus.Ready, RepairStatus.Completed],
            [RepairStatus.Cancelled] = [RepairStatus.Cancelled],
        };

        foreach (var status in path[target])
        {
            order.ChangeStatus(status);
        }

        return order;
    }
}
