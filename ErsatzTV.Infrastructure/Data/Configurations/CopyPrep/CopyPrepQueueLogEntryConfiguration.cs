using ErsatzTV.Core.Domain.CopyPrep;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErsatzTV.Infrastructure.Data.Configurations.CopyPrep;

public class CopyPrepQueueLogEntryConfiguration : IEntityTypeConfiguration<CopyPrepQueueLogEntry>
{
    public void Configure(EntityTypeBuilder<CopyPrepQueueLogEntry> builder)
    {
        builder.ToTable("CopyPrepQueueLogEntry");

        builder.Property(logEntry => logEntry.Level)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(logEntry => logEntry.Event)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(logEntry => logEntry.Message)
            .HasMaxLength(2048)
            .IsRequired();

        builder.HasIndex(logEntry => logEntry.CopyPrepQueueItemId);
        builder.HasIndex(logEntry => logEntry.CreatedAt);
    }
}
