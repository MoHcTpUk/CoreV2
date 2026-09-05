using Shared.Domain.Interfaces;

namespace Sample.Domain.Aggregates;

public class SampleAggregate : IAggregateRoot
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public required Guid Id { get; set; }
}
