namespace ErsatzTV.Application.CopyPrep.Commands;

public record CancelCopyPrepQueueItem(int Id) : IRequest<bool>;
