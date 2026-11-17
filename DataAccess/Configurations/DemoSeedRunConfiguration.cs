using Entities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataAccess.Configurations;

public sealed class DemoSeedRunConfiguration : IEntityTypeConfiguration<DemoSeedRun>
{
    public void Configure(EntityTypeBuilder<DemoSeedRun> builder)
    {
        builder.ToTable("DemoSeedRuns");
        builder.HasKey(x => x.DatasetId);
        builder.Property(x => x.DatasetId).HasMaxLength(80).ValueGeneratedNever();
        builder.Property(x => x.PlanHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PlanJson).IsRequired();
        builder.Property(x => x.CompletedAtUtc).HasColumnType("datetime2");
    }
}
