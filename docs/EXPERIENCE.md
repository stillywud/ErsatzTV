# ErsatzTV 开发和调试经验总结

## 1. 项目编译 (Linux)

### 环境要求
- .NET 10 SDK
- 足够内存（至少 2GB 空闲）

### 编译命令
```bash
# 编译并发布
dotnet publish ErsatzTV/ErsatzTV.csproj -c Release -o ./publish -r linux-x64 --self-contained false -p:MaxCpuCount=1 /m:1

# 如果内存不足，可以分步编译
dotnet build ErsatzTV.Application/ErsatzTV.Application.csproj -c Release --no-restore -p:MaxCpuCount=1 /m:1
dotnet build ErsatzTV.Infrastructure/ErsatzTV.Infrastructure.csproj -c Release --no-restore -p:MaxCpuCount=1 /m:1
dotnet build ErsatzTV/ErsatzTV.csproj -c Release --no-restore -p:MaxCpuCount=1 /m:1
```

### 部署 DLL
```bash
# 复制更新的 DLL 到 publish 目录
cp /opt/ErsatzTV/ErsatzTV/bin/Release/net10.0/ErsatzTV.dll /opt/ErsatzTV/publish/
cp /opt/ErsatzTV/ErsatzTV.Application/bin/Release/net10.0/ErsatzTV.Application.dll /opt/ErsatzTV/publish/
cp /opt/ErsatzTV/ErsatzTV.Infrastructure/bin/Release/net10.0/ErsatzTV.Infrastructure.dll /opt/ErsatzTV/publish/
```

## 2. 调试技巧

### 进程管理
```bash
# 查找占用端口的进程
fuser -k 8409/tcp

# 启动应用（后台运行）
cd /opt/ErsatzTV/publish && nohup ./ErsatzTV > /tmp/etv.log 2>&1 &

# 或使用 timeout（便于查看日志）
timeout 90 ./ErsatzTV
```

### 添加调试日志
在关键位置添加日志：
```csharp
_logger.LogInformation("[SearchIndex] Rebuilding item #{Count}, Type={Type}, Id={Id}", 
    itemCount, mediaItem.GetType().Name, mediaItem.Id);
```

### 数据库查询
```bash
# 检查 SQLite 数据库
sqlite3 /root/.local/share/ersatztv/ersatztv.sqlite3 "SELECT * FROM MediaItem LIMIT 10;"
```

## 3. Bug 修复案例：搜索索引不更新

### 问题描述
扫描完媒体库后，搜索结果为空，但数据库中有数据。

### 根因分析
1. **问题1**：`LuceneSearchIndex.IndexExists()` 只检查目录和 `.clean-shutdown` 文件是否存在，不检查索引是否真的有文档。当索引为空时，版本号检查通过，跳过重建。

2. **问题2**：Scanner 进程发送 reindex HTTP 请求后立即退出，但 HTTP 请求可能还没到达服务器。服务器检查 `IsActive(scanId)` 返回 false，reindex 被丢弃。

### 修复方案

#### 修复1：IndexExists() 增加文档数量检查
```csharp
// LuceneSearchIndex.cs
public Task<bool> IndexExists()
{
    bool directoryExists = Directory.Exists(FileSystemLayout.SearchIndexFolder);
    bool fileExists = File.Exists(_cleanShutdownPath);

    if (!directoryExists || !fileExists)
        return Task.FromResult(false);

    // 检查索引是否有文档
    try
    {
        using var tempDir = FSDirectory.Open(FileSystemLayout.SearchIndexFolder);
        using var tempWriter = new IndexWriter(tempDir, new IndexWriterConfig(...));
        return Task.FromResult(tempWriter.MaxDoc > 0);
    }
    catch
    {
        return Task.FromResult(false);
    }
}
```

#### 修复2：添加 scan-complete 通知机制
1. Scanner 进程结束时调用 `NotifyScanComplete()` 通知服务器
2. 服务器收到后调用 `EndScan()` 标记扫描完成
3. 这样确保所有 reindex 请求在 EndScan 之前处理完成

修改的文件：
- `ErsatzTV/Controllers/Api/ScannerController.cs` - 添加 `/scan-complete` 端点
- `ErsatzTV.Scanner/Core/Interfaces/IScannerProxy.cs` - 添加 `NotifyScanComplete()` 接口
- `ErsatzTV.Scanner/Core/ScannerProxy.cs` - 实现 `NotifyScanComplete()`
- `ErsatzTV.Scanner/Worker.cs` - 扫描完成后调用 `NotifyScanComplete()`
- `ErsatzTV.Application/MediaSources/Commands/CallLocalLibraryScannerHandler.cs` - 不在 handler 中调用 EndScan

### 验证方法
```bash
# 重置搜索索引版本并清空索引目录
sqlite3 /root/.local/share/ersatztv/ersatztv.sqlite3 "UPDATE ConfigElement SET Value = '0' WHERE Key = 'search_index.version';"
rm -rf /root/.local/share/ersatztv/search-index/*

# 重启应用，观察日志
# 应该在日志中看到 "Migrating search index to version 50" 和 "Search index rebuilt with X items"
```

## 4. 本地化配置

### 默认语言和主题
修改文件：`ErsatzTV.Application/Configuration/Queries/GetUiSettingsHandler.cs`
```csharp
return new UiSettingsViewModel
{
    IsDarkMode = await pagesIsDarkMode.IfNoneAsync(false),  // 默认浅色模式
    Language = await pagesLanguage.IfNoneAsync("zh-Hans")  // 默认简体中文
};
```

### 添加新语言支持
1. 修改 `ErsatzTV/Localization.cs`：
```csharp
public static readonly List<CultureLanguage> SupportedLanguages =
[
    new("en-us", "English"),
    new("zh-Hans", "简体中文"),
    new("pl", "Polski"),
    new("pt-br", "Português (Brasil)")
];

public static string DefaultCulture => "zh-Hans";
```

2. 创建翻译资源文件：
   - `Locals/Shared/MainLayout.zh-Hans.resx`
   - `Locals/Shared/Common.zh-Hans.resx`
   - `Locals/Pages/Channels.zh-Hans.resx`

3. 编译后复制资源 DLL：
```bash
cp /opt/ErsatzTV/ErsatzTV/bin/Release/net10.0/zh-Hans/ErsatzTV.resources.dll /opt/ErsatzTV/publish/zh-Hans/
```

### 翻译文件格式
```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
    <data name="ButtonChannels" xml:space="preserve">
        <value>频道</value>
    </data>
    <data name="ButtonSettings" xml:space="preserve">
        <value>设置</value>
    </data>
</root>
```

### 本地化使用方式
在 .razor 页面中注入 localizer：
```razor
@inject IStringLocalizer<ErsatzTV.Locals.Shared.MainLayout> StringLocalizer

<MudNavLink Href="channels">@StringLocalizer["ButtonChannels"]</MudNavLink>
```

## 5. Git 提交规范

```bash
# 查看变更
git status
git diff

# 只提交代码文件（排除临时脚本和 publish 目录）
git add ErsatzTV.Application/Configuration/Queries/GetUiSettingsHandler.cs
git add ErsatzTV/Localization.cs
git add ErsatzTV/Locals/Pages/Channels.zh-Hans.resx
git add ErsatzTV/Locals/Shared/Common.zh-Hans.resx
git add ErsatzTV/Locals/Shared/MainLayout.zh-Hans.resx
git add ErsatzTV.Infrastructure/Search/LuceneSearchIndex.cs
# ... 其他修改的文件

# 提交
git commit -m "描述变更内容"
```

## 6. 常用命令

```bash
# 查找特定代码
grep -r "SearchIndex" --include="*.cs"

# 查看项目结构
ls -la

# 检查日志
tail -f /tmp/etv.log

# 数据库操作
sqlite3 /root/.local/share/ersatztv/ersatztv.sqlite3
```
