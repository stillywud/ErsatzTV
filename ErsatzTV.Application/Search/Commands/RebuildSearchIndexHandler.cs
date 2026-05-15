using System.Diagnostics;
using ErsatzTV.Core;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Interfaces.Metadata;
using ErsatzTV.Core.Interfaces.Repositories;
using ErsatzTV.Core.Interfaces.Search;
using Humanizer;
using Microsoft.Extensions.Logging;

namespace ErsatzTV.Application.Search;

public class RebuildSearchIndexHandler : IRequestHandler<RebuildSearchIndex>
{
    private readonly IConfigElementRepository _configElementRepository;
    private readonly IFallbackMetadataProvider _fallbackMetadataProvider;
    private readonly ILanguageCodeService _languageCodeService;
    private readonly ILocalFileSystem _localFileSystem;
    private readonly ILogger<RebuildSearchIndexHandler> _logger;
    private readonly ISearchIndex _searchIndex;
    private readonly ISearchRepository _searchRepository;
    private readonly SystemStartup _systemStartup;

    public RebuildSearchIndexHandler(
        ISearchIndex searchIndex,
        ISearchRepository searchRepository,
        IConfigElementRepository configElementRepository,
        ILocalFileSystem localFileSystem,
        IFallbackMetadataProvider fallbackMetadataProvider,
        ILanguageCodeService languageCodeService,
        SystemStartup systemStartup,
        ILogger<RebuildSearchIndexHandler> logger)
    {
        _searchIndex = searchIndex;
        _logger = logger;
        _searchRepository = searchRepository;
        _configElementRepository = configElementRepository;
        _localFileSystem = localFileSystem;
        _fallbackMetadataProvider = fallbackMetadataProvider;
        _languageCodeService = languageCodeService;
        _systemStartup = systemStartup;
    }

    public async Task Handle(RebuildSearchIndex request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[RebuildSearchIndex] Starting search index initialization");

        bool indexExists = await _searchIndex.IndexExists();
        _logger.LogInformation("[RebuildSearchIndex] IndexExists returned: {IndexExists}", indexExists);

        if (!await _searchIndex.Initialize(_localFileSystem, _configElementRepository, cancellationToken))
        {
            indexExists = false;
            _logger.LogWarning("[RebuildSearchIndex] Initialize returned false, setting indexExists to false");
        }

        _logger.LogInformation("[RebuildSearchIndex] Done initializing search index");

        int currentVersion = await _configElementRepository.GetValue<int>(ConfigElementKey.SearchIndexVersion, cancellationToken)
            .Match(x => x, () => 0);
        _logger.LogInformation("[RebuildSearchIndex] Current index version: {CurrentVersion}, Target version: {TargetVersion}", currentVersion, _searchIndex.Version);

        if (!indexExists || currentVersion < _searchIndex.Version)
        {
            _logger.LogInformation("[RebuildSearchIndex] Migrating search index to version {Version}", _searchIndex.Version);

            var sw = Stopwatch.StartNew();
            await _searchIndex.Rebuild(
                _searchRepository,
                _fallbackMetadataProvider,
                _languageCodeService,
                cancellationToken);

            await _configElementRepository.Upsert(
                ConfigElementKey.SearchIndexVersion,
                _searchIndex.Version,
                cancellationToken);

            // 记录上次重建时间
            await _configElementRepository.Upsert(
                ConfigElementKey.SearchIndexLastRebuild,
                DateTime.UtcNow,
                cancellationToken);

            sw.Stop();

            _logger.LogInformation("[RebuildSearchIndex] Done migrating search index in {Duration}", sw.Elapsed.Humanize());
        }
        else
        {
            _logger.LogInformation("[RebuildSearchIndex] Search index is already version {Version}", _searchIndex.Version);
        }

        _systemStartup.SearchIndexIsReady();
        _logger.LogInformation("[RebuildSearchIndex] Search index is ready");
    }
}
