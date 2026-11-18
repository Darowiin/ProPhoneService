using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Api.Contracts;

/// <summary>
/// Запрос смены статуса заказа
/// </summary>
/// <param name="Status">Целевой статус</param>
public record ChangeStatusRequest(
    RepairStatus Status);
