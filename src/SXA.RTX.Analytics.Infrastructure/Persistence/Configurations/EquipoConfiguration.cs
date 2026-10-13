using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SXA.RTX.Analytics.Domain.Entities;

namespace SXA.RTX.Analytics.Infrastructure.Persistence.Configurations;

public sealed class EquipoConfiguration : IEntityTypeConfiguration<Equipo>
{
    public void Configure(EntityTypeBuilder<Equipo> b)
    {
        b.ToTable("SXA_RTX_Equipos");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.DeviceId).IsUnique();
        b.Property(x => x.DeviceId).HasMaxLength(64).IsRequired();
        b.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
        b.Property(x => x.Area).HasMaxLength(100).IsRequired();
        b.Property(x => x.Descripcion).HasMaxLength(500);
    }
}

public sealed class TablaConfigConfiguration : IEntityTypeConfiguration<TablaConfig>
{
    public void Configure(EntityTypeBuilder<TablaConfig> b)
    {
        b.ToTable("SXA_RTX_TablasConfig");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.TableName).IsUnique();
        b.Property(x => x.TableName).HasMaxLength(260).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(200);
        b.HasMany(x => x.Columnas)
            .WithOne()
            .HasForeignKey(x => x.TablaConfigId)
            .OnDelete(DeleteBehavior.ClientCascade);
    }
}

public sealed class TablaColumnaConfiguration : IEntityTypeConfiguration<TablaColumna>
{
    public void Configure(EntityTypeBuilder<TablaColumna> b)
    {
        b.ToTable("SXA_RTX_TablaColumnas");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TablaConfigId, x.ColumnName }).IsUnique();
        b.Property(x => x.ColumnName).HasMaxLength(200).IsRequired();
    }
}
