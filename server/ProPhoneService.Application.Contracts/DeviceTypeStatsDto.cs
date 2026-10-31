using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Количество завершённых ремонтов по типу устройства
/// </summary>
/// <param name="Type">Тип устройства</param>
/// <param name="Count">Количество завершённых ремонтов</param>
public record DeviceTypeStatsDto(DeviceType Type, int Count);
