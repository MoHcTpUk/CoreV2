using Sample.Contracts.Commands;
using Sample.Contracts.Dtos;
using Sample.Domain.Aggregates;
using Shared.Application;

namespace Sample.Application;

public class MappingProfile : BaseMappingProfile
{
    public MappingProfile()
    {
        CreateMap<CreateSampleCommand, SampleAggregate>()
            .ForMember(x => x.CreatedAt, opt => opt.MapFrom(_ => DateTime.UtcNow));
        CreateMap<UpdateSampleCommand, SampleAggregate>();
        CreateMap<SampleAggregate, SampleDto>();
    }
}
