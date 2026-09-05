using Sample.Domain.Aggregates;
using Shared.Domain.Interfaces;

namespace Sample.Domain;

public interface ISampleRepository : IRepository<SampleAggregate>;
