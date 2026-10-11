using Mapster;
using ProPhoneService.Application.Contracts;
using ProPhoneService.Domain.Model;

namespace ProPhoneService.Application.Mapper;

internal class MappingRegister : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<RepairStatusHistory, RepairStatusHistoryDto>();
        config.NewConfig<Device, DeviceDto>()
            .Map(dest => dest.Type, src => src.Type.ToString());
        config.NewConfig<RepairOrder, RepairOrderListItemDto>()
            .Map(dest => dest.StatusTimeline, src => src.StatusHistory.OrderBy(h => h.ChangedAt));
    }
}
