using Microsoft.Extensions.Options;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Services;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Domain.Shared.Enum;

namespace ProPhoneService.Application.Services;

/// <summary>
/// Создание заявки на ремонт: устройство, заказ и выбранные услуги
/// Без входа заявка создаётся неподтверждённой (<see cref="RepairStatus.PendingContact"/>)
/// и активируется кодом из VK; авторизованный клиент привязывается сразу
/// </summary>
public class OrderCreationService(
    IRepository<Device> deviceRepository,
    IServiceRepository serviceRepository,
    IRepairOrderRepository orderRepository,
    MessengerBindingService messengerBindings,
    TimeProvider timeProvider,
    IOptions<WorkshopOptions> workshopOptions,
    StatusNotificationService statusNotifications)
{
    /// <summary>
    /// Создать заявку на ремонт
    /// </summary>
    /// <param name="request">Данные заявки</param>
    /// <param name="clientId">Клиент из токена или <c>null</c> для гостевой заявки</param>
    /// <returns>Номер заявки (с признаком ожидания кода) или ошибки валидации по полям</returns>
    public async Task<CreateOrderResult> CreateAsync(
        CreateOrderRequest request,
        Guid? clientId,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();

        // Желаемая дата и время визита: конвертация в UTC + проверка рабочих часов
        var preferredVisitAtUtc = request.PreferredVisitAt!.Value.UtcDateTime;
        var visitError = VisitTimeValidator.Validate(
            preferredVisitAtUtc,
            timeProvider.GetUtcNow().UtcDateTime,
            workshopOptions.Value);
        if (visitError is not null)
        {
            errors["PreferredVisitAt"] = [visitError];
        }

        var isGuest = clientId is null;
        if (!PhoneNormalizer.TryNormalize(request.Phone, out var phone))
        {
            errors["Phone"] = ["Укажите телефон в формате +7XXXXXXXXXX"];
        }

        decimal totalPrice = 0;
        List<Service> selectedServices = [];
        var serviceIds = request.ServiceIds?.Distinct().ToArray() ?? [];
        if (serviceIds.Length > 0)
        {
            var found = await serviceRepository.GetByIdsAsync(serviceIds, cancellationToken);
            var foundIds = found.Select(s => s.Id).ToHashSet();
            var missing = serviceIds.Where(id => !foundIds.Contains(id)).ToArray();

            if (missing.Length > 0)
            {
                errors["ServiceIds"] = [$"Не найдены активные услуги: {string.Join(", ", missing)}"];
            }

            selectedServices = [.. found];
            totalPrice = found.Sum(s => s.Price);
        }

        if (errors.Count > 0)
        {
            return CreateOrderResult.Failure(errors);
        }

        var problemDescription = string.IsNullOrWhiteSpace(request.ProblemDescription)
            ? null
            : request.ProblemDescription.Trim();

        var device = new Device
        {
            Id = Guid.NewGuid(),
            Type = request.DeviceType!.Value,
            Manufacturer = request.DeviceManufacturer,
            Model = request.DeviceModel,
        };

        var order = isGuest
            ? RepairOrder.CreatePending(
                device.Id,
                phone,
                preferredVisitAtUtc,
                problemDescription)
            : RepairOrder.Create(
                clientId!.Value,
                device.Id,
                preferredVisitAtUtc,
                problemDescription);

        order.ContactName = request.Name.Trim();
        order.ContactPhone ??= phone;
        order.TotalPrice = totalPrice;

        foreach (var service in selectedServices)
        {
            order.Services.Add(new RepairOrderService
            {
                RepairOrderId = order.Id,
                ServiceId = service.Id,
                Price = service.Price,
            });
        }

        await deviceRepository.AddAsync(device, cancellationToken);
        await orderRepository.AddAsync(order, cancellationToken);
        await orderRepository.SaveChangesAsync(cancellationToken);

        if (!isGuest)
        {
            await statusNotifications.NotifyStatusChangedAsync(order, cancellationToken);
        }

        var link = isGuest
            ? await messengerBindings.CreateForOrderAsync(order.Id, MessengerChannel.Vk, cancellationToken)
            : null;

        return CreateOrderResult.Success(order.Id, isGuest, link, order.OrderNumber);
    }
}
