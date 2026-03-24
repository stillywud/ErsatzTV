namespace ErsatzTV.Core.Domain.CopyPrep;

public enum CopyPrepStatus
{
    Queued = 0,
    Processing = 1,
    Prepared = 2,
    Failed = 3,
    Canceled = 4,
    Replaced = 5,
    Skipped = 6
}
