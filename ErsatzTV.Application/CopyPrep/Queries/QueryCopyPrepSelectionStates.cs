namespace ErsatzTV.Application.CopyPrep.Queries;

public record QueryCopyPrepSelectionStates(
    IReadOnlyCollection<int> MovieIds) : IRequest<List<CopyPrepSelectionStateViewModel>>;
