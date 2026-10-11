namespace ProPhoneService.Application.Contracts;

/// <summary>
/// Публичное представление устройства в ремонте
/// </summary>
/// <param name="Type">Тип устройства</param>
/// <param name="Manufacturer">Производитель</param>
/// <param name="Model">Модель</param>
public record DeviceDto(
    string Type,
    string Manufacturer,
    string Model);
