namespace ErsatzTV.Application.CopyPrep.Queries;

public record QueryCopyPrepSelectionStates(
    IReadOnlyCollection<int> MovieIds,
    IReadOnlyCollection<int> ShowIds,
    IReadOnlyCollection<int> SeasonIds,
    IReadOnlyCollection<int> EpisodeIds) : IRequest<List<CopyPrepSelectionStateViewModel>>;
