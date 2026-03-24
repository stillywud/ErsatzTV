namespace ErsatzTV.Core.Domain.CopyPrep;

public class CopyPrepQueueItem
{
    public int Id { get; set; }

    public int MediaItemId { get; set; }
    public MediaItem MediaItem { get; set; }

    public int MediaVersionId { get; set; }
    public MediaVersion MediaVersion { get; set; }

    public int MediaFileId { get; set; }
    public MediaFile MediaFile { get; set; }

    public CopyPrepStatus Status { get; set; }
    public string Reason { get; set; }
    public string SourcePath { get; set; }
    public string TargetPath { get; set; }
    public string ArchivePath { get; set; }
    public string WorkingPath { get; set; }
    public string LastLogPath { get; set; }
    public string LastCommand { get; set; }
    public string LastError { get; set; }
    public int? LastExitCode { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateTime? ReplacedAt { get; set; }

    public List<CopyPrepQueueLogEntry> LogEntries { get; set; }
}
