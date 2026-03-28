using ErsatzTV.Core.Domain.CopyPrep;

namespace ErsatzTV.Pages;

public static class CopyPrepPageState
{
    public static bool CanCancel(CopyPrepStatus status) =>
        status is CopyPrepStatus.Queued or CopyPrepStatus.Processing;
}
