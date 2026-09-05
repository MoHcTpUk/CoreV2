using Sample.Contracts.Dtos;
using Shared.Api.Query;
using Shared.Dtos;

namespace Sample.Contracts.Queries;

public class SampleQuery : Query<ResultContainer<SampleDto>>
{
    public List<Guid>? Ids { get; set; }

    public string? Name { get; set; }
}
