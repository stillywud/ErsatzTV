using ErsatzTV.Application.MediaCards;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Interfaces.Search;
using ErsatzTV.Core.Search;
using ErsatzTV.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static ErsatzTV.Application.MediaCards.Mapper;

namespace ErsatzTV.Application.Search;

public class QuerySearchIndexMoviesHandler(
    ISearchIndex searchIndex,
    IDbContextFactory<TvContext> dbContextFactory,
    ILogger<QuerySearchIndexMoviesHandler> logger)
    : QuerySearchIndexHandlerBase, IRequestHandler<QuerySearchIndexMovies, MovieCardResultsViewModel>
{
    public async Task<MovieCardResultsViewModel> Handle(
        QuerySearchIndexMovies request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("[QuerySearchIndexMovies] Searching with query: {Query}", request.Query);
        SearchResult searchResult = await searchIndex.Search(
            request.Query,
            string.Empty,
            (request.PageNumber - 1) * request.PageSize,
            request.PageSize,
            cancellationToken);

        logger.LogInformation("[QuerySearchIndexMovies] Search returned {Count} items, total {Total}", searchResult.Items.Count, searchResult.TotalCount);

        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        Option<JellyfinMediaSource> maybeJellyfin = await GetJellyfin(dbContext, cancellationToken);
        Option<EmbyMediaSource> maybeEmby = await GetEmby(dbContext, cancellationToken);

        var ids = searchResult.Items.Map(i => i.Id).ToHashSet();
        logger.LogInformation("[QuerySearchIndexMovies] Found {Count} unique IDs", ids.Count);

        List<MovieCardViewModel> items = await dbContext.MovieMetadata
            .AsNoTracking()
            .Filter(mm => ids.Contains(mm.MovieId))
            .Include(mm => mm.Artwork)
            .Include(mm => mm.Movie)
            .ThenInclude(m => m.MediaVersions)
            .ThenInclude(mv => mv.MediaFiles)
            .OrderBy(mm => mm.SortTitle)
            .ToListAsync(cancellationToken)
            .Map(list => list.Map(m => ProjectToViewModel(m, maybeJellyfin, maybeEmby)).ToList());

        logger.LogInformation("[QuerySearchIndexMovies] Retrieved {Count} items from database", items.Count);

        return new MovieCardResultsViewModel(searchResult.TotalCount, items, searchResult.PageMap);
    }
}
