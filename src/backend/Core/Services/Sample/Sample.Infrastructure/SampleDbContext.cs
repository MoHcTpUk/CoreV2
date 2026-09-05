using Microsoft.EntityFrameworkCore;
using Sample.Domain.Aggregates;

namespace Sample.Infrastructure;

public class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
{
    public DbSet<SampleAggregate> Samples { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SampleAggregate>(entity =>
        {
            entity.ToTable("Samples");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Description);
            entity.Property(e => e.CreatedAt).IsRequired();
        });
    }
}
