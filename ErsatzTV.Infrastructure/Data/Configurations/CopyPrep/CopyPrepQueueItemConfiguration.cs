using ErsatzTV.Core.Domain.CopyPrep;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErsatzTV.Infrastructure.Data.Configurations.CopyPrep;

public class CopyPrepQueueItemConfiguration : IEntityTypeConfiguration<CopyPrepQueueItem>
{
    public void Configure(EntityTypeBuilder<CopyPrepQueueItem> builder)
    {
        builder.ToTable("CopyPrepQueueItem");

        builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(item => item.Reason)
            .HasMaxLength(2048);

        builder.Property(item => item.SourcePath)
            .IsRequired();

        builder.HasIndex(item => item.Status);
        builder.HasIndex(item => item.MediaItemId)
            .IsUnique();

        builder.HasOne(item => item.MediaItem)
            .WithMany()
            .HasForeignKey(item => item.MediaItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.MediaVersion)
            .WithMany()
            .HasForeignKey(item => item.MediaVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.MediaFile)
            .WithMany()
            .HasForeignKey(item => item.MediaFileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(item => item.LogEntries)
            .WithOne(logEntry => logEntry.CopyPrepQueueItem)
            .HasForeignKey(logEntry => logEntry.CopyPrepQueueItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
