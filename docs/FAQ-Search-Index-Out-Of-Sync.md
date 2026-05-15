# ErsatzTV 常见问题：搜索索引与数据库不同步

## 问题描述

**现象**：Web 搜索页面显示为空，但媒体库中实际存在文件。

**具体表现**：
- 访问 `http://192.168.0.66:8409/search?query=library_id%3a20` 返回空结果
- 数据库中确认有 44 个媒体项（`SELECT COUNT(*) FROM MediaItem WHERE LibraryPathId = 17`）
- 搜索索引（Lucene）中只有 168 个文档，而数据库中有 326 个媒体项

## 根本原因

### 1. 搜索索引未正确重建

搜索索引（Lucene）与数据库不同步，导致搜索结果为空。

**技术细节**：
- 搜索索引存储在：`/root/.local/share/ersatztv/search-index/`
- 索引版本检查逻辑：`IndexExists()` 只检查版本号和目录存在性
- 当索引版本（50）与目标版本匹配时，即使文档数量不匹配，也不会触发重建

### 2. Scanner 实时索引机制失效

Scanner 进程在扫描完成后应通过 HTTP API 发送重新索引请求，但存在以下问题：
- Scanner 进程发送 `ReindexMediaItems` 请求后，主进程可能未正确处理
- `IsActive(scanId)` 检查可能导致请求被丢弃
- HTTP 请求可能在 Scanner 进程退出前未完成

### 3. 扫描流程中的问题

**发现的额外问题**：
- Scanner 可执行文件未随主程序一起发布
- Scanner 缺少 SQLite 依赖库（`libe_sqlite3.so`）
- 扫描队列按顺序处理，library 20 的扫描被延迟

## 诊断过程

### 步骤 1：验证搜索索引状态
```bash
# 检查索引目录
ls -la /root/.local/share/ersatztv/search-index/

# 检查索引文档数量（通过日志）
tail /tmp/etv.log | grep "MaxDoc"
```

### 步骤 2：验证数据库状态
```bash
# 检查媒体项总数
sqlite3 /root/.local/share/ersatztv/ersatztv.sqlite3 "SELECT COUNT(*) FROM MediaItem;"

# 检查特定库的媒体项
sqlite3 /root/.local/share/ersatztv/ersatztv.sqlite3 "SELECT COUNT(*) FROM MediaItem WHERE LibraryPathId = 17;"
```

### 步骤 3：添加诊断日志
修改的文件：
- `ErsatzTV.Infrastructure/Search/LuceneSearchIndex.cs` - 添加 `IndexExists()` 和 `Search()` 日志
- `ErsatzTV.Application/Search/Queries/QuerySearchIndexMoviesHandler.cs` - 添加查询日志
- `ErsatzTV.Application/Search/Commands/RebuildSearchIndexHandler.cs` - 添加重建日志

## 解决方案

### 临时解决方案（已执行）

1. **停止服务**
   ```bash
   pkill -f "ErsatzTV"
   ```

2. **清除搜索索引**
   ```bash
   rm -rf /root/.local/share/ersatztv/search-index/*
   ```

3. **重启服务**
   ```bash
   cd /opt/app/ErsatzTV/publish && nohup ./ErsatzTV > /tmp/etv.log 2>&1 &
   ```

4. **验证重建**
   ```bash
   tail /tmp/etv.log | grep "Search index rebuilt"
   # 预期输出：Search index rebuilt with 326 items
   ```

### 修复验证

- [x] 搜索索引文档数：168 → 326
- [x] `library_id:20` 搜索结果：0 → 44 个视频
- [x] 搜索页面正常显示内容

## 相关代码位置

### 搜索索引核心代码
- `ErsatzTV.Infrastructure/Search/LuceneSearchIndex.cs` - Lucene 索引实现
- `ErsatzTV.Application/Search/Commands/RebuildSearchIndexHandler.cs` - 重建逻辑
- `ErsatzTV/Services/SearchIndexService.cs` - 后台索引服务

### Scanner 相关代码
- `ErsatzTV.Scanner/Core/ScannerProxy.cs` - Scanner HTTP 客户端
- `ErsatzTV.Scanner/Worker.cs` - Scanner 主流程
- `ErsatzTV/Controllers/Api/ScannerController.cs` - Scanner API 端点

### 扫描触发代码
- `ErsatzTV.Application/MediaSources/Commands/CallLocalLibraryScannerHandler.cs`
- `ErsatzTV/Services/ScannerService.cs`

## 预防措施

### 1. 监控指标

建议监控以下指标：
- 数据库媒体项数量 vs 搜索索引文档数量
- Scanner 进程退出状态码
- HTTP API 请求成功率（`/api/scan/{scanId}/items/reindex`）

### 2. 定期维护

建议添加定时任务：
```bash
# 每周重建搜索索引
sqlite3 /root/.local/share/ersatztv/ersatztv.sqlite3 "UPDATE ConfigElement SET Value = '0' WHERE Key = 'search_index.version';"
# 然后重启服务
```

### 3. 发布流程改进

确保发布脚本包含：
- Scanner 可执行文件
- 所有依赖的 `.so` 库文件

## 参考链接

- 经验总结文档：`docs/EXPERIENCE.md` - 第 3 节 "Bug 修复案例：搜索索引不更新"
- 相关 Issue：搜索索引版本检查、Scanner 进程通信

---

**记录日期**：2026-05-15
**解决状态**：已解决
**影响版本**：develop 分支
**最后更新**：2026-05-15
