using Sample.Contracts.Dtos;
using Shared.Api.Commands;
using Shared.Dtos;

namespace Sample.Contracts.Commands;

public class CreateSampleCommand : CreateCommand<ResultContainer<SampleDto>>
{
    public required string Name { get; set; }

    public string? Description { get; set; }
}
