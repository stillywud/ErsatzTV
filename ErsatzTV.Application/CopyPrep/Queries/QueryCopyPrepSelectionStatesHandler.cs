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

        List<OtherVideo> otherVideos = await dbContext.OtherVideos
            .AsNoTracking()
            .Where(otherVideo => request.OtherVideoIds.Contains(otherVideo.Id))
            .Include(otherVideo => otherVideo.MediaVersions)
                .ThenInclude(version => version.MediaFiles)
            .Include(otherVideo => otherVideo.MediaVersions)
                .ThenInclude(version => version.Streams)
            .ToListAsync(cancellationToken);

        var result = new List<CopyPrepSelectionStateViewModel>();

        var moviesById = movies.ToDictionary(movie => movie.Id);
        foreach (int movieId in request.MovieIds)
        {
            if (moviesById.TryGetValue(movieId, out Movie movie))
            {
                ProjectSelectionState(result, movie.Id, "movie", movie.MediaVersions);
            }
        }

        var otherVideosById = otherVideos.ToDictionary(otherVideo => otherVideo.Id);
        foreach (int otherVideoId in request.OtherVideoIds)
        {
            if (otherVideosById.TryGetValue(otherVideoId, out OtherVideo otherVideo))
            {
                ProjectSelectionState(result, otherVideo.Id, "other_video", otherVideo.MediaVersions);
            }
        }

        return result;
    }

    private static void ProjectSelectionState(
        List<CopyPrepSelectionStateViewModel> result,
        int mediaItemId,
        string mediaKind,
        List<MediaVersion> versions)
    {
        if (versions.Count == 0)
        {
            return;
        }

        MediaVersion version = versions[0];
        if (version.MediaFiles.Count == 0)
        {
            return;
        }

        MediaFile file = version.MediaFiles.Head();
        CopyPrepDecision decision = CopyPrepAnalyzer.Analyze(version, file.Path);
        CopyPrepSelectionStatus status = decision.ShouldQueue
            ? CopyPrepSelectionStatus.NeedsCopyPrep
            : CopyPrepSelectionStatus.CopyReady;

        result.Add(new CopyPrepSelectionStateViewModel(
            mediaItemId,
            mediaKind,
            status,
            decision.ShouldQueue,
            decision.Summary));
    }
}
