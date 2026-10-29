using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Application.Services;
using ProPhoneService.Domain.Model;
using ProPhoneService.Domain.Repositories;

namespace ProPhoneService.Api.Tests;

public class ServicesEndpointTests
{
    private static readonly Service[] _seedServices =
    [
        new() { Id = Guid.NewGuid(), Name = "Диагностика (выявление неисправности)", Price = 0m, IsActive = true },
        new() { Id = Guid.NewGuid(), Name = "Замена дисплейного модуля", Price = 1500m, IsActive = true },
        new() { Id = Guid.NewGuid(), Name = "Замена аккумулятора", Price = 1500m, IsActive = true },
    ];

    private static Service[] CurrentServices() => [.. _seedServices];

    /// <summary>
    /// Фабрика с подменённым IServiceRepository: источник данных, который можно мутировать между вызовами,
    /// со счётчиком обращений к источнику (для проверки кэш-попаданий)
    /// </summary>
    private class MutablePriceSource(Service[] services)
    {
        public Service[] Services { get; set; } = services;

        public int RepositoryCalls { get; private set; }

        public WebApplicationFactory<Program> CreateFactory()
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.ConfigureTestServices(servicesCollection =>
                {
                    var repository = new Mock<IServiceRepository>();
                    repository
                        .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(() =>
                        {
                            RepositoryCalls++;
                            return Services;
                        });

                    servicesCollection.RemoveAll<IServiceRepository>();
                    servicesCollection.AddScoped(_ => repository.Object);
                });
            });
        }
    }

    [Fact]
    public async Task GetAll_Returns200WithSeededServices()
    {
        using var factory = new MutablePriceSource(CurrentServices()).CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/services");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var services = await response.Content.ReadFromJsonAsync<List<ServiceDto>>();
        Assert.NotNull(services);
        Assert.Equal(3, services.Count);
        Assert.All(services, s => Assert.NotEqual(Guid.Empty, s.Id));
        Assert.Contains(services, s => s.Name == "Диагностика (выявление неисправности)" && s.Price == 0m);
        Assert.Contains(services, s => s.Name == "Замена дисплейного модуля" && s.Price == 1500m);
    }

    [Fact]
    public async Task GetAll_CachesAfterFirstCall_RepositoryHitOnceForTwoRequests()
    {
        var source = new MutablePriceSource(CurrentServices());
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient();

        var first = await client.GetAsync("/api/services");
        var second = await client.GetAsync("/api/services");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // Второй запрос обслужен из кэша: источник данных дёрган ровно один раз
        Assert.Equal(1, source.RepositoryCalls);
    }

    [Fact]
    public async Task GetAll_AfterSourceChange_StillReturnsStaleData_UntilInvalidated()
    {
        var source = new MutablePriceSource(CurrentServices());
        using var factory = source.CreateFactory();
        using var client = factory.CreateClient();

        // Первый запрос — прогрев кэша
        var first = await client.GetAsync("/api/services");
        var firstList = await first.Content.ReadFromJsonAsync<List<ServiceDto>>();
        Assert.Equal(3, firstList!.Count);

        // Источник меняется (мастер отредактировал прайс напрямую в БД)
        source.Services =
        [
            new() { Id = Guid.NewGuid(), Name = "Срочный ремонт", Price = 9999m, IsActive = true },
        ];

        // Без инвалидации кэш отдаёт старые данные — TTL 5 минут ещё не истёк
        var stale = await client.GetAsync("/api/services");
        var staleList = await stale.Content.ReadFromJsonAsync<List<ServiceDto>>();
        Assert.Equal(3, staleList!.Count);
        Assert.DoesNotContain(staleList, s => s.Name == "Срочный ремонт");

        // Инвалидация по тегу — следующий запрос уходит в источник и видит новое
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var priceList = scope.ServiceProvider.GetRequiredService<PriceListService>();
            await priceList.InvalidateAsync();
        }

        var fresh = await client.GetAsync("/api/services");
        var freshList = await fresh.Content.ReadFromJsonAsync<List<ServiceDto>>();
        Assert.Single(freshList!);
        Assert.Equal("Срочный ремонт", freshList![0].Name);
        Assert.Equal(9999m, freshList[0].Price);
    }

    /// <summary>
    /// Хранилище услуг для CRUD-тестов: mock-репозиторий, хранящий состояние между вызовами
    /// </summary>
    private class ServiceStore
    {
        private static Service CloneService(Service s) => new()
        {
            Id = s.Id,
            Name = s.Name,
            Description = s.Description,
            Price = s.Price,
            IsActive = s.IsActive,
        };

        public List<Service> Services { get; set; } = [];

        public Service? CreatedService { get; private set; }

        public List<Service> UpdatedServices { get; } = [];

        public WebApplicationFactory<Program> CreateFactory()
        {
            Services = [.. Services.Select(CloneService)];

            return new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.ConfigureTestServices(services =>
                {
                    var repository = new Mock<IServiceRepository>();
                    repository
                        .Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(() => Services
                            .Where(s => s.IsActive)
                            .OrderBy(s => s.Name)
                            .ToList());

                    repository
                        .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((Guid id, CancellationToken _) => Services.FirstOrDefault(s => s.Id == id));

                    repository
                        .Setup(r => r.AddAsync(It.IsAny<Service>(), It.IsAny<CancellationToken>()))
                        .Callback((Service s, CancellationToken _) =>
                        {
                            Services.Add(s);
                            CreatedService = s;
                        })
                        .Returns(Task.CompletedTask);

                    repository
                        .Setup(r => r.Update(It.IsAny<Service>()))
                        .Callback((Service s) =>
                        {
                            UpdatedServices.Add(s);
                            var index = Services.FindIndex(x => x.Id == s.Id);
                            if (index >= 0)
                            {
                                Services[index] = s;
                            }
                        });

                    repository
                        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(1);

                    services.RemoveAll<IServiceRepository>();
                    services.AddScoped(_ => repository.Object);
                });
            });
        }
    }

    [Fact]
    public async Task Create_ValidService_Returns201WithDto()
    {
        var store = new ServiceStore();
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/services", new
        {
            name = "Замена стекла",
            description = "Стекло и рамка",
            price = 900m,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ServiceDto>();
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal("Замена стекла", dto.Name);
        Assert.Equal("Стекло и рамка", dto.Description);
        Assert.Equal(900m, dto.Price);

        // Услуга прошла через вставку репозитория
        Assert.NotNull(store.CreatedService);
    }

    [Fact]
    public async Task Update_ExistingService_Returns200WithUpdatedDto()
    {
        var store = new ServiceStore { Services = [.. _seedServices] };
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient();

        var target = _seedServices[0];
        var response = await client.PutAsJsonAsync($"/api/services/{target.Id}", new
        {
            name = target.Name,
            description = "Диагностика устройства перед ремонтом",
            price = 700m,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ServiceDto>();
        Assert.NotNull(dto);
        Assert.Equal(target.Id, dto.Id);
        Assert.Equal(700m, dto.Price);
        Assert.Equal("Диагностика устройства перед ремонтом", dto.Description);

        var updated = Assert.Single(store.UpdatedServices);
        Assert.Equal(700m, updated.Price);
    }

    [Fact]
    public async Task Update_UnknownService_Returns404()
    {
        using var factory = new ServiceStore().CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/services/{Guid.NewGuid()}", new
        {
            name = "Тест",
            price = 500m,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Hide_ExistingService_SoftDeletesViaIsActive()
    {
        var store = new ServiceStore { Services = [.. _seedServices] };
        using var factory = store.CreateFactory();
        using var client = factory.CreateClient();

        var target = _seedServices[1]; // активная услуга
        Assert.True(target.IsActive);

        var response = await client.DeleteAsync($"/api/services/{target.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var mutated = Assert.Single(store.UpdatedServices);
        Assert.False(mutated.IsActive);
    }

    [Fact]
    public async Task Hide_UnknownService_Returns404()
    {
        using var factory = new ServiceStore().CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/api/services/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_NegativePrice_Returns400WithPriceError()
    {
        using var factory = new ServiceStore().CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/services", new
        {
            name = "Тест",
            price = -100m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(problem);
        Assert.True(problem.RootElement.TryGetProperty("errors", out var errors) && errors.TryGetProperty("Price", out _));
    }

    [Fact]
    public async Task Create_InvalidatesCache_NewServiceVisibleInList()
    {
        var store = new ServiceStore { Services = [.. _seedServices] };
        using var client = store.CreateFactory().CreateClient();

        // Прогрев кэша
        var warm = await client.GetAsync("/api/services");
        Assert.Equal(3, (await warm.Content.ReadFromJsonAsync<List<ServiceDto>>())!.Count);

        var create = await client.PostAsJsonAsync("/api/services", new
        {
            name = "Замена стекла",
            description = (string?)"Стекло и рамка",
            price = 900m,
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var fresh = await client.GetAsync("/api/services");
        var items = await fresh.Content.ReadFromJsonAsync<List<ServiceDto>>();
        Assert.NotNull(items);
        Assert.Equal(4, items.Count);
        Assert.Contains(items, s => s.Name == "Замена стекла" && s.Price == 900m);
    }

    [Fact]
    public async Task Update_InvalidatesCache_FreshPriceVisibleInList()
    {
        var store = new ServiceStore { Services = [.. _seedServices] };
        using var client = store.CreateFactory().CreateClient();

        var warm = await client.GetAsync("/api/services");

        var target = _seedServices[0]; // "Диагностика", цена 0
        var update = await client.PutAsJsonAsync($"/api/services/{target.Id}", new
        {
            name = target.Name,
            description = (string?)"Диагностика устройства перед ремонтом",
            price = 700m,
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var fresh = await client.GetAsync("/api/services");
        var items = await fresh.Content.ReadFromJsonAsync<List<ServiceDto>>();
        Assert.NotNull(items);
        Assert.Equal(3, items.Count);
        Assert.DoesNotContain(items, s => s.Id == target.Id && s.Price != 700m);
    }

    [Fact]
    public async Task Hide_InvalidatesCache_HiddenServiceGoneFromList()
    {
        var store = new ServiceStore { Services = [.. _seedServices] };
        using var client = store.CreateFactory().CreateClient();

        var warm = await client.GetAsync("/api/services");
        Assert.Equal(3, (await warm.Content.ReadFromJsonAsync<List<ServiceDto>>())!.Count);

        var target = _seedServices[1]; // "Замена дисплейного модуля"
        var response = await client.DeleteAsync($"/api/services/{target.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var fresh = await client.GetAsync("/api/services");
        var items = await fresh.Content.ReadFromJsonAsync<List<ServiceDto>>();
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, s => s.Id == target.Id || s.Name == "Замена дисплейного модуля");
    }
}
