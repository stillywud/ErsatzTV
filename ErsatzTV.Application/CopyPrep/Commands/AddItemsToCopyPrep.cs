namespace ErsatzTV.Application.CopyPrep.Commands;

public record AddItemsToCopyPrep(IReadOnlyCollection<int> MovieIds) : IRequest<AddItemsToCopyPrepResult>;
