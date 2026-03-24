namespace ErsatzTV.Application.CopyPrep.Queries;

public record GetCopyPrepQueueItems(int Limit = 100) : IRequest<List<CopyPrepQueueItemViewModel>>;
