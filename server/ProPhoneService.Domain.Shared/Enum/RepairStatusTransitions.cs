namespace ProPhoneService.Domain.Shared.Enum;

/// <summary>
/// Карта допустимых переходов статусов заказа на ремонт (машина состояний)
/// </summary>
public static class RepairStatusTransitions
{
    private static readonly Dictionary<RepairStatus, RepairStatus[]> _map = new()
    {
        [RepairStatus.Created] = [RepairStatus.InDiagnostic, RepairStatus.Cancelled],
        [RepairStatus.InDiagnostic] = [RepairStatus.Agreed],
        [RepairStatus.Agreed] = [RepairStatus.InProgress, RepairStatus.Cancelled],
        [RepairStatus.InProgress] = [RepairStatus.Ready, RepairStatus.Cancelled],
        [RepairStatus.Ready] = [RepairStatus.Completed],
        [RepairStatus.Completed] = [],
        [RepairStatus.Cancelled] = [],
    };

    /// <summary>
    /// Проверить, допустим ли переход из одного статуса в другой
    /// </summary>
    /// <param name="from">Текущий статус</param>
    /// <param name="to">Целевой статус</param>
    /// <returns><c>true</c>, если переход допустим</returns>
    public static bool IsAllowed(RepairStatus from, RepairStatus to)
    {
        return _map.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    /// <summary>
    /// Получить все статусы, достижимые из заданного
    /// </summary>
    /// <param name="from">Текущий статус</param>
    public static IReadOnlyCollection<RepairStatus> GetAllowed(RepairStatus from)
    {
        return _map.TryGetValue(from, out var allowed)
            ? Array.AsReadOnly(allowed)
            : Array.Empty<RepairStatus>();
    }
}
