# 中文说明 | English Documentation

---

## 项目概述 | Project Overview

ErsatzTV 是一个让你将媒体库转变为个性化直播电视体验的工具，支持 EPG（电子节目指南）、频道排程和无缝流媒体到所有设备。

**核心功能：**
- 自定义电视频道和节目时间表
- IPTV 和 EPG 支持
- 硬件转码 (NVENC, QSV, VAAPI, AMF, VideoToolbox)
- 媒体服务器集成 (Plex, Jellyfin, Emby)
- 音乐视频和字幕支持

---

## 项目结构 | Project Structure

```
ErsatzTV/
├── ErsatzTV/                    # 主 Web 应用 (ASP.NET Core Blazor)
├── ErsatzTV.Application/        # 应用层 (Commands, Queries, Handlers)
├── ErsatzTV.Core/               # 核心业务逻辑
├── ErsatzTV.Core.Nullable/      # 可空类型支持
├── ErsatzTV.FFmpeg/             # FFmpeg 封装
├── ErsatzTV.Infrastructure/     # 基础设施抽象
├── ErsatzTV.Infrastructure.Sqlite/ # SQLite 数据库支持
├── ErsatzTV.Infrastructure.MySql/  # MySQL 数据库支持
├── ErsatzTV.Scanner/            # 媒体扫描服务
├── ErsatzTV.Tests/              # 单元测试
├── build.sh                     # 构建脚本
└── publish/                     # 发布产物
```

### 依赖关系 | Dependencies

```
ErsatzTV (Web UI)
    ↓
ErsatzTV.Application
    ↓
ErsatzTV.Core / ErsatzTV.Core.Nullable
    ↓
ErsatzTV.FFmpeg / ErsatzTV.Infrastructure
    ↓
ErsatzTV.Scanner
```

---

## 环境要求 | Requirements

- **.NET 10.0 SDK** 或更高版本
- **PostgreSQL** / **MySQL** / **SQLite** (三选一)
- **FFmpeg** (可选，用于转码)

---

## 快速开始 | Quick Start

### 1. 克隆项目 | Clone Project

```bash
git clone https://github.com/ersatztv/ersatztv.git
cd ersatztv
```

### 2. 构建项目 | Build Project

```bash
# Debug 模式 (快速，包含调试符号)
./build.sh build

# Release 模式 (优化后，性能更好)
./build.sh release build
```

### 3. 发布 | Publish

```bash
# 发布为独立可执行文件
./build.sh publish

# 输出目录: publish/release/
```

### 4. 运行 | Run

```bash
cd ErsatzTV
dotnet run
# 或指定端口
dotnet run --urls "http://localhost:5000"
```

---

## 构建脚本详解 | Build Script Guide

### 使用方法 | Usage

```bash
./build.sh [debug|release] [build|publish]
```

### 参数说明 | Parameters

| 参数 | 说明 |
|------|------|
| `debug` | Debug 模式，带调试符号，构建快 |
| `release` | Release 模式，优化后，性能好 |
| `build` | 仅构建项目 |
| `build publish` | 构建并发布为单文件 |

### 示例 | Examples

```bash
# Debug 构建
./build.sh build

# Release 构建
./build.sh release build

# Release 发布 (最常用)
./build.sh publish

# Debug 发布
./build.sh debug publish
```

### 输出目录 | Output

- Debug: `publish/debug/`
- Release: `publish/release/`

---

## 本地化 | Localization

ErsatzTV 支持多语言界面，采用 `IStringLocalizer` 模式进行本地化。

### 文件结构 | File Structure

```
Locals/
├── Pages/
│   ├── Channels.resx           # 英文 (默认)
│   ├── Channels.zh-Hans.resx   # 简体中文
│   ├── Channels.Designer.cs   # 自动生成
│   └── ...
└── Shared/
```

### 添加新语言支持 | Adding New Language

1. 创建 `.resx` 文件，命名格式: `{PageName}.{culture}.resx`
2. 在文件中添加翻译键值对
3. 在页面中添加 `@inject IStringLocalizer<Namespace> Loc`

### 当前支持的语言 | Currently Supported Languages

- English (en-US) - 默认
- 简体中文 (zh-Hans)
- Polish (pl)
- Portuguese Brazil (pt-br)

---

## 数据库配置 | Database Configuration

ErsatzTV 支持三种数据库后端：

### SQLite (开发/小型部署)

```json
{
  "DatabaseDriver": "sqlite",
  "ConnectionString": "Data Source=ersatztv.db"
}
```

### PostgreSQL (推荐生产环境)

```json
{
  "DatabaseDriver": "postgres",
  "ConnectionString": "Host=localhost;Database=ersatztv;Username=postgres;Password=password"
}
```

### MySQL

```json
{
  "DatabaseDriver": "mysql",
  "ConnectionString": "Server=localhost;Database=ersatztv;Uid=root;Pwd=password"
}
```

---

## Docker 部署 | Docker Deployment

```bash
# 使用 SQLite
docker run -d \
  --name ersatztv \
  -p 8080:80 \
  -v /path/to/media:/media \
  -v ersatztv_data:/data \
  ersatztv/ersatztv

# 使用 PostgreSQL
docker run -d \
  --name ersatztv \
  -p 8080:80 \
  -e DB_DRIVER=postgres \
  -e DB_CONNECTION_STRING="Host=db;Database=ersatztv;Username=postgres;Password=password" \
  -v /path/to/media:/media \
  ersatztv/ersatztv
```

---

## 常见问题 | FAQ

### Q: 构建失败，内存不足
A: 使用 `-p:MaxParallelism=1` 参数减少并行构建，或增加系统内存

### Q: 如何启用硬件转码？
A: 在 FFmpeg 配置文件中选择支持的硬件编码器 (NVENC/QSV/VAAPI)

### Q: 如何备份数据？
A: 备份数据库文件 (`.db`) 和配置目录 (`~/.ersatztv/` 或 Docker 数据卷)

---

## 贡献 | Contributing

欢迎提交 Pull Request！请确保：
1. 代码遵循项目现有的 C# 代码风格
2. 所有测试通过
3. 新功能包含相应的单元测试

---

## 许可证 | License

本项目基于 zlib 许可证开源。

---

## 链接 | Links

- 官方网站: https://ersatztv.org
- 文档: https://ersatztv.org/docs/
- 功能投票: https://features.ersatztv.org/
- 社区讨论: https://discuss.ersatztv.org/
