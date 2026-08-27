using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atmos.Web.Data.Configurations;

public sealed class RecentSearchConfiguration : IEntityTypeConfiguration<RecentSearch>
{
    public const int MaxLabelLength = 200;

    public void Configure(EntityTypeBuilder<RecentSearch> builder)
    {
        builder.ToTable("RecentSearch", t =>
        {
            t.HasCheckConstraint("CK_RecentSearch_Latitude", "[Latitude] BETWEEN -90 AND 90");
            t.HasCheckConstraint("CK_RecentSearch_Longitude", "[Longitude] BETWEEN -180 AND 180");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.SessionId)
            .HasColumnType("char(32)")
            .IsRequired();

        builder.Property(r => r.Label)
            .HasMaxLength(MaxLabelLength)
            .IsRequired();

        builder.Property(r => r.Units)
            .HasConversion<string>()
            .HasMaxLength(10)
            .HasDefaultValue(UnitsPreference.Imperial);

        builder.Property(r => r.LocationType)
            .HasConversion<string>()
            .HasMaxLength(10)
            .HasDefaultValue(LocationType.Zip);

        builder.Property(r => r.CreatedUtc)
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(r => r.LastAccessedUtc)
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(r => new { r.SessionId, r.Label })
            .IsUnique();

        builder.HasIndex(r => new { r.SessionId, r.LastAccessedUtc })
            .HasDatabaseName("IX_RecentSearch_SessionId_LastAccessedUtc")
            .IsDescending(false, true)
            .IncludeProperties(r => new
            {
                r.Label,
                r.Latitude,
                r.Longitude,
                r.ElevationMeters,
                r.Units,
                r.LocationType
            });
    }
}
