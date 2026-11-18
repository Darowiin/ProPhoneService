using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Количество выданных заказов по типу устройства
/// </summary>
/// <param name="Type">Тип устройства</param>
/// <param name="Count">Количество выданных заказов</param>
public record DeviceTypeStatsDto(DeviceType Type, int Count);
