# 搜索索引消息队列实现

## 问题背景

之前的架构中，Scanner 进程通过 HTTP API 直接写入 Channel 来触发搜索索引更新。这种方式存在以下问题：

1. **消息丢失风险**：如果主进程重启，Channel 中的消息会丢失
2. **Scanner 退出检测问题**：`IsActive(scanId)` 检查可能在 Scanner 快速退出时误判，导致消息被丢弃
3. **无持久化**：索引更新请求没有持久化存储，无法追溯和恢复

## 解决方案

使用 SQLite 消息队列替代内存 Channel，实现可靠的消息传递。

## 实现详情

### 1. 数据库表结构

```sql
CREATE TABLE SearchIndexQueue (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    MediaItemId INTEGER NOT NULL,
    Operation INTEGER NOT NULL,  -- 0=Reindex, 1=Remove
    CreatedAt TEXT NOT NULL,
    Processed INTEGER NOT NULL DEFAULT 0,
    ProcessedAt TEXT NULL
);

-- 索引
CREATE INDEX IX_SearchIndexQueue_CreatedAt ON SearchIndexQueue(CreatedAt);
CREATE INDEX IX_SearchIndexQueue_MediaItemId ON SearchIndexQueue(MediaItemId);
CREATE INDEX IX_SearchIndexQueue_Processed ON SearchIndexQueue(Processed);
CREATE INDEX IX_SearchIndexQueue_ProcessedAt ON SearchIndexQueue(ProcessedAt);
```

### 2. 核心组件

#### SearchIndexQueueItem (领域模型)
```csharp
public class SearchIndexQueueItem
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public SearchIndexOperation Operation { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Processed { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
```

#### ISearchIndexQueueRepository (接口)
```csharp
public interface ISearchIndexQueueRepository
{
    Task EnqueueReindexRequest(List<int> mediaItemIds, CancellationToken cancellationToken);
    Task EnqueueRemoveRequest(List<int> mediaItemIds, CancellationToken cancellationToken);
    Task<List<SearchIndexQueueItem>> GetPendingRequests(int batchSize, CancellationToken cancellationToken);
    Task MarkAsProcessed(List<int> requestIds, CancellationToken cancellationToken);
    Task<int> GetPendingCount(CancellationToken cancellationToken);
    Task CleanupProcessedRequests(TimeSpan retentionPeriod, CancellationToken cancellationToken);
}
```

### 3. 流程变更

**旧流程：**
```
Scanner → HTTP API → Channel → SearchIndexService → 索引更新
```

**新流程：**
```
Scanner → HTTP API → SQLite Queue → SearchIndexService → 索引更新
```

### 4. 关键变更文件

| 文件 | 变更说明 |
|------|----------|
| `ScannerController.cs` | 改为写入 SQLite 队列而非 Channel |
| `SearchIndexService.cs` | 改为从 SQLite 队列读取而非 Channel |
| `SearchIndexQueueRepository.cs` | 新增队列操作实现 |
| `SearchIndexQueueItem.cs` | 新增队列项实体 |
| `SearchIndexOperation.cs` | 新增操作类型枚举 |
| `ISearchIndexQueueRepository.cs` | 新增 Repository 接口 |
| `TvContext.cs` | 添加 SearchIndexQueue DbSet |
| `Startup.cs` | 注册 ISearchIndexQueueRepository |

### 5. 批处理机制

SearchIndexService 使用批处理提高效率：

- **最大批次大小**：100 条
- **最大等待时间**：10 秒
- **定期清理**：每 24 小时清理已处理超过 7 天的记录

### 6. 优势

1. **持久化**：消息存储在 SQLite 中，服务重启不丢失
2. **可靠性**：即使 Scanner 立即退出，消息也已写入数据库
3. **可追溯**：可以查询待处理请求数量和历史记录
4. **自动清理**：自动清理过期记录，防止表无限增长

## 迁移说明

部署此更新时需要：

1. 运行数据库迁移（自动创建 SearchIndexQueue 表）
2. 重启服务后，新的索引更新请求将使用队列机制
3. 旧的 Channel 机制被完全替换，无需额外配置

## 监控

可以通过以下方式监控队列状态：

```csharp
// 获取待处理请求数量
int pendingCount = await searchIndexQueueRepository.GetPendingCount(cancellationToken);
```

日志输出示例：
```
Search index worker service started (using SQLite queue)
Processing search index batch. Reindexing: 50, Removing: 0
Cleaned up 100 old search index queue items
```
