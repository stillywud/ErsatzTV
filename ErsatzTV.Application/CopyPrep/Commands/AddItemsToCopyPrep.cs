namespace ErsatzTV.Application.CopyPrep.Commands;

public record AddItemsToCopyPrep(
    IReadOnlyCollection<int> MovieIds,
    IReadOnlyCollection<int> OtherVideoIds) : IRequest<AddItemsToCopyPrepResult>;
