using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Interfaces.Repositories;
using ErsatzTV.Core.Interfaces.Search;

namespace ErsatzTV.Application.Search;

public record CheckSearchIndexHealth : IRequest<SearchIndexHealthViewModel>;

public record SearchIndexHealthViewModel(
    bool IsHealthy,
    int DatabaseCount,
    int IndexCount,
    int Difference,
    DateTime? LastRebuild);

public class CheckSearchIndexHealthHandler(
    ISearchIndex searchIndex,
    ISearchRepository searchRepository,
    IConfigElementRepository configElementRepository)
    : IRequestHandler<CheckSearchIndexHealth, SearchIndexHealthViewModel>
{
    public async Task<SearchIndexHealthViewModel> Handle(
        CheckSearchIndexHealth request,
        CancellationToken cancellationToken)
    {
        // 获取数据库中的媒体项数量
        int dbCount = await searchRepository.GetAllMediaItemsCount(cancellationToken);

        // 获取搜索索引中的文档数量
        int indexCount = searchIndex.GetDocumentCount();

        // 获取上次重建时间
        var lastRebuild = await configElementRepository.GetValue<DateTime>(
            ConfigElementKey.SearchIndexLastRebuild,
            cancellationToken);

        // 计算差异
        int difference = Math.Abs(dbCount - indexCount);

        // 判断是否健康（差异小于1%或绝对值小于10）
        bool isHealthy = difference == 0 || (difference < 10 && dbCount > 0 && (double)difference / dbCount < 0.01);

        return new SearchIndexHealthViewModel(
            IsHealthy: isHealthy,
            DatabaseCount: dbCount,
            IndexCount: indexCount,
            Difference: difference,
            LastRebuild: lastRebuild.Match(dt => dt, (DateTime?)null));
    }
}
