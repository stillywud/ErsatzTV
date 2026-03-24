using ErsatzTV.Core.Domain.CopyPrep;

namespace ErsatzTV.Application.CopyPrep;

public record CopyPrepQueueLogEntryViewModel(
    int Id,
    DateTime CreatedAt,
    string Level,
    string Event,
    string Message,
    string Details);

public record CopyPrepQueueItemViewModel(
    int Id,
    int MediaItemId,
    CopyPrepStatus Status,
    string Reason,
    string SourcePath,
    string TargetPath,
    string ArchivePath,
    string LastLogPath,
    string LastError,
    int? LastExitCode,
    int AttemptCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime QueuedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? FailedAt,
    DateTime? ReplacedAt,
    List<CopyPrepQueueLogEntryViewModel> LogEntries);
