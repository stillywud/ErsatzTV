namespace ErsatzTV.Application.CopyPrep.Queries;

public record QueryCopyPrepSelectionStates(
    IReadOnlyCollection<int> MovieIds,
    IReadOnlyCollection<int> OtherVideoIds) : IRequest<List<CopyPrepSelectionStateViewModel>>;
