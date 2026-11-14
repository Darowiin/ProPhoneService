using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Domain.Shared;

/// <summary>
/// Русские названия статусов заявки для интерфейса
/// </summary>
public static class RepairStatusNames
{
    /// <summary>
    /// Получить название статуса или пустую строку для неинициализированной заявки
    /// </summary>
    public static string GetName(RepairStatus? status) => status switch
    {
        RepairStatus.PendingContact => "Ожидает подтверждения",
        RepairStatus.Created => "Заявка принята",
        RepairStatus.InProgress => "В ремонте",
        RepairStatus.Ready => "Готов к выдаче",
        RepairStatus.Completed => "Выдан клиенту",
        RepairStatus.Cancelled => "Заявка отменена",
        null => "",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
