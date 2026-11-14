using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Repositories;
using ProPhoneService.Domain.Shared;
using ProPhoneService.Infrastructure.EfCore;
using ProPhoneService.Infrastructure.EfCore.Notifications;

namespace ProPhoneService.Api.Tests;

public class NotifierRegistrationTests
{
    [Fact]
    public void AddNotifier_RegistersMessengerRouterAndVkProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new MessengerOptions());
        services.AddSingleton(Mock.Of<IMessengerBindingRepository>());
        services.AddNotifier();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();
        var vk = scope.ServiceProvider.GetRequiredService<VkNotifier>();

        Assert.IsType<MessengerNotifier>(notifier);
        Assert.NotNull(vk);
    }
}
