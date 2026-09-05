using System.Linq.Expressions;
using AutoMapper;
using Sample.Contracts.Dtos;
using Sample.Contracts.Queries;
using Sample.Domain;
using Sample.Domain.Aggregates;
using Shared.Application.Handlers;

namespace Sample.Application.Handlers;

public class GetSampleHandler(ISampleRepository repository, IMapper mapper)
    : GetAggregateHandler<SampleAggregate, SampleDto, SampleQuery>(repository, mapper)
{
    protected override List<(Func<SampleQuery, bool> Condition, Expression<Func<SampleAggregate, bool>> Filter)>
        FilterExpression(SampleQuery request)
    {
        return
        [
            (query => query.Ids != null && query.Ids.Count != 0, x => request.Ids!.Contains(x.Id)),
            (query => !string.IsNullOrWhiteSpace(query.Name), x => x.Name.Contains(request.Name!))
        ];
    }
}
