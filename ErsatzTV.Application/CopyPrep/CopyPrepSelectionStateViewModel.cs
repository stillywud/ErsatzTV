namespace ErsatzTV.Application.CopyPrep;

public enum CopyPrepSelectionStatus
{
    CopyReady,
    NeedsCopyPrep
}

public record CopyPrepSelectionStateViewModel(
    int MediaItemId,
    string MediaKind,
    CopyPrepSelectionStatus Status,
    bool IsSelectable,
    string Reason);
