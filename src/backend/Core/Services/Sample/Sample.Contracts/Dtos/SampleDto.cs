using Shared.Dtos;

namespace Sample.Contracts.Dtos;

public record SampleDto : Dto
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
}
