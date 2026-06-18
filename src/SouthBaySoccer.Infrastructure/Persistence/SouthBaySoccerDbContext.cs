using Microsoft.EntityFrameworkCore;

namespace SouthBaySoccer.Infrastructure.Persistence;

/// <summary>
/// EF Core <see cref="DbContext"/> for SouthBaySoccer and the unit-of-work boundary for
/// aggregate persistence. DbSets and entity type configurations are added as the Domain
/// layer reference is wired in.
/// </summary>
public class SouthBaySoccerDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SouthBaySoccerDbContext"/> class.
    /// </summary>
    /// <param name="options">The options to configure the context.</param>
    public SouthBaySoccerDbContext(DbContextOptions<SouthBaySoccerDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply IEntityTypeConfiguration<T> from this assembly here
    }
}
