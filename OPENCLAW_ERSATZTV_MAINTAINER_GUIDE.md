# ErsatzTV 本地维护与编译指南（OpenClaw）

## 目标

这份文档用于让后续的 OpenClaw agent、其它工具，或者人工维护者，能够继续在当前这台 Win10 机器上：

- 理解 `D:\project\ErsatzTV` 的本地环境状态
- 继续编译、测试、发布这个项目
- 尽量复用已经准备好的离线依赖
- 在修改源码后按稳定流程回归验证

---

## 1. 项目位置

源码仓库：

- `D:\project\ErsatzTV`

核心文件：

- `ErsatzTV.sln`
- `global.json`
- `ErsatzTV\ErsatzTV.csproj`
- `ErsatzTV.Scanner\ErsatzTV.Scanner.csproj`
- `.github\workflows\pr.yml`
- `.github\workflows\artifacts.yml`

---

## 2. 当前本机环境状态

### 已完成
- `.NET SDK 10.0.201` 已安装
- `dotnet --info` 已验证成功
- `.NET 10` 离线安装包和安装日志已保存到本地

### 离线资产目录
- `D:\project\ErsatzTV\build-deps\dotnet-sdk`

其中包含：
- 官方 EXE 安装器
- 官方 ZIP 包
- `releases-10.0.json`
- 校验结果 JSON
- 主安装日志与 MSI 子日志
- 安装后的 `dotnet --info` 快照

详细说明见：
- `OPENCLAW_DOTNET10_INSTALL_2026-03-24.md`

---

## 3. 项目实际要求

从仓库当前状态确认：

- 目标框架：`net10.0`
- `global.json` 要求 SDK：`10.0.0`，`rollForward=latestMinor`
- 当前已安装 `10.0.201`，满足要求

---

## 4. 当前推荐工作流

### A. 先恢复依赖
```powershell
dotnet restore
```

如果要贴近 Windows 发布目标，可使用：

```powershell
dotnet restore -r win-x64
```

### B. 再构建
```powershell
dotnet build --configuration Release --no-restore
```

### C. 再测试
```powershell
dotnet test --blame-hang-timeout "2m" --no-restore --verbosity normal
```

### D. 如需发布
参考 CI 的方式发布 `Scanner` 与 `ErsatzTV` 主程序。

---

## 5. 一个关键坑：主项目里的 Scanner 引用

仓库 CI 在构建前，会对：

- `ErsatzTV\ErsatzTV.csproj`

做一个预处理：

- 删除其中的 `ErsatzTV.Scanner` 项目引用行

CI 中对应做法（Linux/macOS 用 `sed`）大意是：

```bash
sed -i '/Scanner/d' ErsatzTV/ErsatzTV.csproj
```

### 为什么要特别记这个
因为这说明：

- 仓库的常规 CI 就是这样做的
- 后续本地构建也应该沿用这个思路
- 但**不要永久手改并忘记恢复**

### 当前建议
优先使用仓库外部的自动脚本，而不是直接手改 csproj。

本地已新增脚本：

- `C:\Users\intel1230\skills\ersatztv-source-build\scripts\build_ersatztv.ps1`

这个脚本会：

- 先备份 `ErsatzTV.csproj`
- 临时移除 `Scanner` 引用
- 执行 restore/build/test/publish
- 最后自动恢复原始 csproj

---

## 6. 推荐日志策略

后续所有正式构建，建议都输出到日志目录，而不是只看终端。

推荐目录：

- `D:\project\ErsatzTV\build-logs`

这样每次 restore/build/test/publish 都有独立日志，便于：

- 查错
- 复盘
- 让后续 agent 快速接手

---

## 7. OpenClaw Skill

已经创建项目专用 skill：

- `C:\Users\intel1230\skills\ersatztv-source-build`

用途：

- 当后续 agent 遇到 `D:\project\ErsatzTV` 相关任务时
- 可以按这个 skill 提供的上下文、脚本、流程来继续维护

如果需要分发/复用，可直接使用已打包的 skill 文件：

- `C:\Users\intel1230\skills\dist\ersatztv-source-build.skill`

---

## 8. 后续维护者最应该先看的文档

按优先级：

1. `OPENCLAW_ERSATZTV_MAINTAINER_GUIDE.md`（本文件）
2. `OPENCLAW_BUILD_PREP_2026-03-24.md`
3. `OPENCLAW_DOTNET10_INSTALL_2026-03-24.md`
4. `.github\workflows\pr.yml`
5. `.github\workflows\artifacts.yml`

---

## 9. 目前最值得继续推进的事

在 SDK 已就绪的前提下，下一阶段重点是：

1. 跑通第一次 `dotnet restore`
2. 跑通 `dotnet build`
3. 跑通 `dotnet test`
4. 如需要，再做 `publish win-x64`
5. 尽可能把 NuGet 依赖也固化成更可离线复用的状态

---

## 10. 一句话总结

> 当前这台机器已经具备 ErsatzTV 的 .NET 10 本地编译前提；后续维护时，优先使用项目 skill 和自动脚本，不要手改 `ErsatzTV.csproj` 后忘记恢复。
