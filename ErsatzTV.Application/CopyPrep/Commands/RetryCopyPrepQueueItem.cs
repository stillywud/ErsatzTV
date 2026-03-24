namespace ErsatzTV.Application.CopyPrep.Commands;

public record RetryCopyPrepQueueItem(int Id) : IRequest<bool>;
