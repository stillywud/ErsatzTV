using ErsatzTV.Core.CopyPrep;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Extensions;
using ErsatzTV.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ErsatzTV.Application.CopyPrep.Queries;

public class QueryCopyPrepSelectionStatesHandler(IDbContextFactory<TvContext> dbContextFactory)
    : IRequestHandler<QueryCopyPrepSelectionStates, List<CopyPrepSelectionStateViewModel>>
{
    public async Task<List<CopyPrepSelectionStateViewModel>> Handle(
        QueryCopyPrepSelectionStates request,
        CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        List<Movie> movies = await dbContext.Movies
            .AsNoTracking()
            .Where(movie => request.MovieIds.Contains(movie.Id))
            .Include(movie => movie.MediaVersions)
                .ThenInclude(version => version.MediaFiles)
            .Include(movie => movie.MediaVersions)
                .ThenInclude(version => version.Streams)
            .ToListAsync(cancellationToken);

        var moviesById = movies.ToDictionary(movie => movie.Id);
        var result = new List<CopyPrepSelectionStateViewModel>();

        foreach (int movieId in request.MovieIds)
        {
            if (!moviesById.TryGetValue(movieId, out Movie movie))
            {
                continue;
            }

            if (movie.MediaVersions.Count == 0)
            {
                continue;
            }

            MediaVersion version = movie.GetHeadVersion();
            if (version.MediaFiles.Count == 0)
            {
                continue;
            }

            MediaFile file = version.MediaFiles.Head();
            CopyPrepDecision decision = CopyPrepAnalyzer.Analyze(version, file.Path);
            CopyPrepSelectionStatus status = decision.ShouldQueue
                ? CopyPrepSelectionStatus.NeedsCopyPrep
                : CopyPrepSelectionStatus.CopyReady;

            result.Add(new CopyPrepSelectionStateViewModel(
                movie.Id,
                "movie",
                status,
                decision.ShouldQueue,
                decision.Summary));
        }

        return result;
    }
}
