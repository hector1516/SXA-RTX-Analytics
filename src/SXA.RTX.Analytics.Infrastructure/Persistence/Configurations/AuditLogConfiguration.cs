using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SXA.RTX.Analytics.Domain.Entities;

namespace SXA.RTX.Analytics.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TimestampUtc).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(200);
        builder.Property(x => x.PerformedBy).HasMaxLength(200);
        builder.Property(x => x.Details).HasMaxLength(4000);
        builder.Property(x => x.IpAddress).HasMaxLength(45);
        builder.HasIndex(x => x.TimestampUtc);
    }
}
