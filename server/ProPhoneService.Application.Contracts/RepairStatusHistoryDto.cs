using ProPhoneService.Domain.Shared.Enum;
using ProPhoneService.Domain.Shared;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Публичное представление записи истории смены статуса заказа
/// </summary>
/// <param name="Status">Статус, в который перешёл заказ</param>
/// <param name="ChangedAt">Дата и время смены статуса (UTC)</param>
public record RepairStatusHistoryDto(
    RepairStatus Status,
    DateTime ChangedAt)
{
    /// <summary>
    /// Русское название статуса в истории
    /// </summary>
    public string StatusName => RepairStatusNames.GetName(Status);
}
