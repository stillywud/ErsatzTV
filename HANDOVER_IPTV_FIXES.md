# ErsatzTV IPTV 卡顿问题修复交接文档

## 文档目的

记录 ErsatzTV 项目从普通 HLS 模式迁移到 concat 模式过程中遇到的问题、修复方案、踩过的坑，以及验证方法。供后续维护和继续优化参考。

---

## 1. 问题背景

### 1.1 原始问题
- IPTV 播放器（如 iptvnator）打开频道时有约 10 秒卡顿
- 频道切换不流畅，缓冲时间长
- 雍正王朝（channel 10）换集时出现卡顿、错乱、花屏

### 1.2 目标
- 实现"秒开"频道切换
- 使用 concat 模式实现无缝剧集切换
- 消除换集时的卡顿和花屏

---

## 2. 关键概念

### 2.1 HLS 模式 vs Concat 模式

| 模式 | 说明 | 适用场景 |
|------|------|----------|
| HttpLiveStreamingSegmenter (4) | 普通 HLS，每个剧集单独生成片段 | 单文件播放 |
| HttpLiveStreamingConcat (6) | Concat 模式，FFmpeg 通过 concat 协议连接多个剧集 | 多剧集连续播放 |

### 2.2 Concat 模式工作原理
1. 外层 FFmpeg 读取 concat 文件（包含多个 HTTP URL）
2. 每个 URL 指向 `/ffmpeg/stream/{channel}`，返回当前播放项的 MPEG-TS 流
3. 当一集播放完毕，FFmpeg 自动切换到下一个 URL，获取下一集
4. 实现无缝过渡

### 2.3 关键文件
- `ErsatzTV.Core/FFmpeg/ConcatPlaylist.cs` - concat 文件生成
- `ErsatzTV.Application/Streaming/HlsConcatSessionWorker.cs` - concat 模式会话管理
- `ErsatzTV.FFmpeg/Pipeline/PipelineBuilderBase.cs` - FFmpeg 命令生成
- `ErsatzTV/Services/ChannelPreloadService.cs` - 频道预加载服务

---

## 3. 修复历程

### 3.1 修复 1：Readrate 不匹配（PipelineBuilderBase.cs）

**问题**：Concat 模式和 Segmenter 模式的 readrate 不一致
- Concat 模式使用 `-readrate 1.0`
- Segmenter 模式使用 `-readrate 1.05`

**后果**：速度不匹配导致轻微卡顿和花屏

**修复**：统一为 1.05
```csharp
// Concat 方法
concatInputFile.AddOption(new ReadrateInputOption(1.05));

// WrapSegmenter 方法
concatInputFile.AddOption(new ReadrateInputOption(1.05));
```

### 3.2 修复 2：Playlist 与磁盘文件不同步（HlsSessionWorker.cs）

**问题**：Playlist 返回已删除的片段

**修复**：
- 增加删除间隔从 30 秒到 300 秒
- 添加 `AdjustPlaylistToMatchDiskFiles()` 方法过滤 playlist

### 3.3 修复 3：返回最早而非最新片段（HlsPlaylistFilter.cs）

**问题**：`Take(maxSegments)` 返回最早片段

**修复**：改为 `TakeLast(maxSegments)` 返回最新片段

### 3.4 修复 4：Concat 模式换集卡顿（ConcatPlaylist.cs + HlsConcatSessionWorker.cs）

**问题**：
- `ConcatPlaylist` 只有 2 个 URL 条目
- FFmpeg 播完后退出，导致 HLS 流中断
- 工作目录被清空，播放列表重新开始
- 换集时出现 discontinuity

**修复 1 - ConcatPlaylist.cs**：
```csharp
// 从 2 个条目增加到 100 个
for (int i = 0; i < 100; i++)
{
    sb.AppendLine(url);
}
```

**修复 2 - HlsConcatSessionWorker.cs**：
```csharp
// 移除 FFmpeg 退出后的等待时间
// 原代码：await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
// 新代码：立即重启
```

### 3.5 修复 5：ChannelPreloadService 模式选择

**问题**：预加载服务硬编码使用 `segmenter` 模式，不支持 concat 模式

**修复**：根据频道配置自动选择模式
```csharp
string mode = channel.StreamingMode switch
{
    StreamingMode.HttpLiveStreamingConcat => "segmenter-concat",
    _ => "segmenter"
};
```

### 3.6 修复 6：Trim playlist failure（HlsConcatSessionWorker.cs）

**问题**：
- FFmpeg 崩溃重启期间，`live.m3u8` 文件不存在
- `TrimAndDelete` 方法返回 `None`，导致播放器无法获取播放列表
- 出现大量 `Trim playlist failure; will return not found for channel 10` 错误

**修复**：
```csharp
// 当 playlist 文件不存在时，返回空 playlist 而不是 None
if (!_fileSystem.File.Exists(playlistPath))
{
    return new TrimPlaylistResult(
        DateTimeOffset.MinValue, 0, 0, 
        "#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:4\n", 
        0);
}
```

### 3.7 修复 7：HlsConcatSessionWorker 重启时保留目录（HlsConcatSessionWorker.cs）

**问题**：
- FFmpeg 进程重启时，`EmptyFolder` 会清空工作目录
- 导致播放列表中断，客户端看到 discontinuity

**修复**：
```csharp
// 第一次运行时创建目录，后续重启时不清空
if (isFirstRun)
{
    _localFileSystem.EnsureFolderExists(_workingDirectory);
    _localFileSystem.EmptyFolder(_workingDirectory);
}
```

### 3.8 修复 8：禁用预取避免竞争条件（HlsSessionWorker.cs）

**问题**：
- `TryStartPreFetch` 在剧集快结束时预取下一集
- 但 playout 每 30 分钟重建一次，预取可能获取到旧数据
- 导致时间戳不匹配，播放错乱

**修复**：
```csharp
// 禁用预取，避免与 playout 重建竞争
private void TryStartPreFetch(...)
{
    return; // 禁用预取
}
```

### 3.9 修复 9：TranscodeCleanupService 误删活跃频道目录（TranscodeCleanupService.cs）

**问题**：
- 清理服务仅根据目录修改时间判断是否活跃
- 某些情况下会误删正在播放的频道目录
- 导致播放中断

**修复**：
```csharp
// 同时检查是否有 FFmpeg 进程在写入该目录
bool hasActiveFFmpeg = HasActiveFFmpegProcess(dirName);
if (dirInfo.LastWriteTimeUtc > DateTime.UtcNow.AddMinutes(-5) || hasActiveFFmpeg)
{
    // 目录最近有修改或有活跃 FFmpeg，不清理
}
```

---

## 4. 踩过的坑

### 4.1 代码被还原
- **现象**：修改后测试正常，但后续发现代码被还原
- **原因**：git 工作目录中有未提交的修改，服务重启后加载了旧版本
- **解决**：每次修改后必须提交并推送

### 4.2 服务启动的是 publish 目录的旧 DLL
- **现象**：修改了源码但服务行为未变
- **原因**：服务启动的是 `/opt/app/ErsatzTV/publish/ErsatzTV` 而不是编译后的 DLL
- **解决**：使用 `dotnet /opt/app/ErsatzTV/ErsatzTV/bin/Release/net10.0/ErsatzTV.dll` 启动

### 4.3 publish 目录缺少依赖文件
- **现象**：从 publish 目录启动时报 `FileNotFoundException`
- **原因**：publish 目录未包含所有依赖
- **解决**：使用 `dotnet run` 或直接引用编译后的 DLL

### 4.4 Concat 文件条目数量不足
- **现象**：换集时 FFmpeg 退出，HLS 流中断
- **原因**：只有 2 个条目，FFmpeg 很快播完
- **解决**：增加到 100 个条目

### 4.5 数据库模式未更新
- **现象**：频道预加载使用错误的模式
- **原因**：数据库中 StreamingMode 字段未更新
- **解决**：直接修改 SQLite 数据库
```sql
UPDATE Channel SET StreamingMode = 6 WHERE StreamingMode != 6;
```

---

## 5. 验证方法（如何确认服务已更新）

### 5.1 验证 Concat 文件条目数
```bash
curl -s "http://localhost:8409/ffmpeg/concat/10?mode=ts-legacy" | wc -l
# 预期输出：101（1 行版本声明 + 100 行文件条目）
```

### 5.2 验证 Readrate
```bash
ps aux | grep "ffmpeg.*readrate" | grep "channel.*10"
# 预期看到：-readrate 1.05
```

### 5.3 验证频道模式
```bash
sqlite3 /root/.local/share/ersatztv/ersatztv.sqlite3 \
  "SELECT Number, Name, StreamingMode FROM Channel WHERE Number = '10'"
# 预期输出：10|雍正王朝|6
```

### 5.4 验证服务进程
```bash
ps aux | grep -i ersatz | grep -v grep
# 确认启动的是编译后的 DLL，不是 publish 目录
```

### 5.5 验证 HLS 播放列表
```bash
curl -s "http://localhost:8409/iptv/session/10/hls.m3u8" | head -20
# 确认返回有效的片段列表
```

---

## 6. 测试方法

### 6.1 手动测试
1. 使用 iptvnator 或 VLC 打开频道
2. 观察频道切换是否秒开
3. 等待剧集结束，观察换集是否卡顿
4. 检查画面是否有花屏

### 6.2 日志监控
```bash
# 实时监控频道 10 的日志
tail -f /tmp/ersatztv.log | grep -E "channel 10|雍正|concat"
```

### 6.3 关键日志检查
- `Starting HLS concat session` - concat 会话启动
- `HLS concat process failed` - 进程失败（应不常见）
- `Terminating HLS session` - 会话终止（应不常见）

---

## 7. 后续关注

### 7.1 潜在问题
1. **Concat 文件 100 个条目是否足够**：如果一集时长很短，100 个条目可能不够
2. **内存使用**：大量条目可能增加内存使用
3. **HTTP 连接复用**：每次重新打开 URL 是否高效

### 7.2 优化方向
1. 动态生成 concat 文件条目数（根据剧集时长计算）
2. 添加剧集切换时的 discontinuity 标记
3. 优化预加载策略，减少启动时间

### 7.3 监控指标
- 频道切换时间
- 换集卡顿次数
- FFmpeg 进程重启频率
- HLS 播放列表 discontinuity 次数

---

## 8. 快速参考

### 8.1 常用命令
```bash
# 构建
dotnet build -c Release --nologo

# 启动服务（使用编译后的 DLL）
nohup dotnet /opt/app/ErsatzTV/ErsatzTV/bin/Release/net10.0/ErsatzTV.dll \
  --urls http://0.0.0.0:8409 > /tmp/ersatztv.log 2>&1 &

# 检查服务状态
curl -s http://localhost:8409/health

# 查看日志
tail -f /tmp/ersatztv.log

# 检查进程
ps aux | grep -i ersatz | grep -v grep

# 检查 FFmpeg 进程
ps aux | grep ffmpeg | grep -v grep | wc -l
```

### 8.2 关键文件路径
- 源码：`/opt/app/ErsatzTV/`
- 编译输出：`/opt/app/ErsatzTV/ErsatzTV/bin/Release/net10.0/`
- 日志：`/tmp/ersatztv.log`
- 转码目录：`/vol3/etv-transcode/`
- 数据库：`/root/.local/share/ersatztv/ersatztv.sqlite3`

---

## 9. 更新记录

| 日期 | 修改内容 | 提交 |
|------|----------|------|
| 2026-05-25 | Fix readrate mismatch (1.0 -> 1.05) | 29da9ac0 |
| 2026-05-25 | Fix concat mode episode transitions | 29da9ac0 |
| 2026-05-25 | Fix channel preloading for concat mode | 29da9ac0 |
| 2026-05-25 | Increase concat entries from 2 to 100 | 29da9ac0 |
| 2026-05-25 | Remove 2s delay in HlsConcatSessionWorker | 29da9ac0 |
| 2026-05-30 | Fix Trim playlist failure | 8ae870bb |
| 2026-05-30 | Disable pre-fetch to avoid race condition | 8ae870bb |
| 2026-05-30 | Fix TranscodeCleanupService deleting active channels | 8ae870bb |

---

## 10. 一句话总结

> Concat 模式通过生成 100 个 URL 条目让 FFmpeg 持续运行，避免换集时退出；同时统一 readrate、修复预加载模式、修复 Trim playlist failure，实现无缝剧集切换。
