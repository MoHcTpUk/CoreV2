using Sample.Domain;
using Sample.Domain.Aggregates;
using Shared.Infrastructure.Repository;

namespace Sample.Infrastructure.Repositories;

public class SampleRepository(SampleDbContext context)
    : AbstractRepository<SampleAggregate, SampleDbContext>(context), ISampleRepository;
