using Microsoft.EntityFrameworkCore;
using Identity.Domain.Aggregates;

namespace Identity.Infrastructure;

public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationUser> Users { get; set; }

    public DbSet<AuthTicket> AuthTickets { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.UserName).IsUnique();
        });

        modelBuilder.Entity<AuthTicket>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Redemption and the sweep of dead tickets both look the row up by expiry.
            entity.HasIndex(e => e.ExpiresAt);
        });

        // OpenIddict entities
        modelBuilder.UseOpenIddict();
    }
}
