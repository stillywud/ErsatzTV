# ErsatzTV 当前进度与源码编译前置条件（2026-03-24）

## 一、今晚已完成的工作总结

### 1. 已完成 HLS copy 预处理工具的独立化
已创建独立目录：

- `D:\apps\ErsatzTV-HLS-Copy-Prep`

当前目录中包含：

- `start_hls_copy_prep.bat`
- `hls_copy_prep_final.py`
- `README-使用说明.md`
- `python-3.14.3-amd64.exe`（本地 Python 安装包）
- `bin\ffmpeg.exe`
- `bin\ffprobe.exe`
- `source\`
- `target\`
- `done\`
- `logs\`

### 2. 工具已具备的能力
- 菜单式 BAT 入口
- 环境检查
- 预检查模式（只扫描，不转码）
- 正式预处理模式
- 本地 Python 安装包检测与引导安装
- 转码完成后自动把 `source` 中原文件移动到 `done`
- 如果发现 `target` 中成品已存在且有效，则不重复转码，只把 `source` 中残留原文件移动到 `done`

### 3. 已验证过的修复
在以下现场状态下进行了验证：

- `source\01.mp4` 仍存在
- `target\01.mp4` 已经存在且完整

新版工具的行为是：

- **不重复转码**
- 直接把 `source\01.mp4` 移动到 `done\01.mp4`

对应日志：

- `D:\apps\ErsatzTV-HLS-Copy-Prep\logs\run-20260324-224849-summary.json`

状态字段为：

- `recovered_existing_target`

这说明“目标文件已有效存在，只做归档修复，不做重复转码”的逻辑已经生效。

---

## 二、当前源码项目信息

项目路径：

- `D:\project\ErsatzTV`

关键文件：

- `ErsatzTV.sln`
- `global.json`
- `README.md`
- `.github\workflows\ci.yml`
- `.github\workflows\pr.yml`
- `.github\workflows\artifacts.yml`

项目结构显示这是一个 **.NET 解决方案**，核心项目包括：

- `ErsatzTV`
- `ErsatzTV.Scanner`
- `ErsatzTV.Application`
- `ErsatzTV.Core`
- `ErsatzTV.Infrastructure*`
- 多个 `*.Tests`

---

## 三、源码编译要求（根据仓库与 CI 实际确认）

### 1. .NET SDK 要求
`global.json` 明确要求：

```json
{
  "sdk": {
    "version": "10.0.0",
    "rollForward": "latestMinor",
    "allowPrerelease": false
  }
}
```

结论：

- **需要 .NET SDK 10.0.x**
- 不是 .NET 8 / .NET 9
- 当前机器上还 **没有检测到 `dotnet`**

当前检测结果：

- `dotnet`: **NOT FOUND**

这是目前本机开始编译的**第一阻塞项**。

### 2. 目标框架
关键项目文件显示：

- `ErsatzTV\ErsatzTV.csproj` → `TargetFramework = net10.0`
- `ErsatzTV.Scanner\ErsatzTV.Scanner.csproj` → `TargetFramework = net10.0`

因此必须先满足 `.NET 10 SDK`。

### 3. Node / npm / pnpm
从当前仓库和 CI 工作流来看：

- **没有发现构建前必须执行的前端 Node 打包步骤**
- `ErsatzTV.csproj` 中有 `TypeScriptCompileBlocked=true`
- GitHub Actions 也只使用 `dotnet restore / build / test / publish`

结论：

- **当前阶段本地编译大概率不需要 Node/npm/pnpm/yarn**

### 4. Git / 其它工具当前状态
当前机器检测结果：

- `git`: 已存在
- `7z`: 未找到
- `msbuild`: 未找到
- `nuget`: 未找到

说明：

- **本地编译源码本身，主要靠 `dotnet` 即可**
- `msbuild`、`nuget.exe`、`7z` 不是第一步的硬阻塞
- 但如果后面要做“接近官方 Windows 发布包”的完整打包，则可能需要 `7z`

---

## 四、官方 CI / 仓库里的实际构建方式

### 1. PR / CI 常规构建
根据 `.github\workflows\pr.yml`：

标准流程大致是：

1. `actions/setup-dotnet` 安装 `10.0.x`
2. `dotnet clean --configuration Release`
3. `dotnet nuget locals all --clear`
4. `dotnet restore`
5. 对 `ErsatzTV\ErsatzTV.csproj` 做一个预处理：
   - 删除其中的 `Scanner` 引用行
6. `dotnet build --configuration Release --no-restore`
7. `dotnet test --no-restore`

### 2. Windows 发布构建
根据 `.github\workflows\artifacts.yml`：

Windows 发布逻辑大致是：

1. `dotnet restore -r win-x64`
2. 预处理 `ErsatzTV.csproj`（删除 Scanner 引用）
3. 发布两个项目：
   - `ErsatzTV.Scanner/ErsatzTV.Scanner.csproj`
   - `ErsatzTV/ErsatzTV.csproj`
4. 使用 `--framework net10.0`
5. 使用 `--runtime win-x64`
6. `-c Release`
7. `-p:PublishSingleFile=true`
8. `--self-contained true`

说明：

- **本地完全可以先只做到“源码可 restore / build / test / publish”**
- 不必第一步就追求完全复刻官方带签名的最终发布包

---

## 五、要不要处理 submodule
`.gitmodules` 显示只有一个子模块：

- `ErsatzTV-macOS`

这主要用于 macOS 打包流程。

结论：

- **当前 Win10 本地构建，不是第一优先级**
- Windows 侧源码可编译环境，理论上不依赖这个 submodule 先成立

---

## 六、如果目标只是“建立源码可编译 / 可改造环境”，当前最小条件

只要满足下面这些，就能开始推进：

### 必需条件
1. **安装 .NET SDK 10.0.x（x64，Windows）**
2. **机器能访问 NuGet 包源**（因为需要 `dotnet restore`）
3. 仓库路径可正常读写（当前已满足）

### 可能会用到但不是首阻塞
1. `7z`（如果后面要做 Windows 发布包整包压缩）
2. 外部下载资源（如果后面想完全复刻官方 Windows ZIP 发布物）
   - `ErsatzTV-Windows.exe`
   - ffmpeg Windows 包

### 当前不一定需要
1. Node / npm
2. Visual Studio 完整 IDE
3. Azure 签名凭据
4. Apple 签名相关内容

---

## 七、当前真正阻塞点

### 阻塞点 1：本机没有 dotnet
当前执行：

- `dotnet --info`

结果：

- `DOTNET_NOT_FOUND`

这是现在最明确的阻塞点。

### 阻塞点 2：后面可能需要网络恢复 NuGet 包
即使装好了 .NET SDK，如果机器不能访问 NuGet，`dotnet restore` 也会失败。

所以还需要确认：

- 本机是否允许联网拉取 NuGet 依赖

---

## 八、下一步我准备怎么做

一旦 `.NET 10 SDK` 到位，我会按这个顺序推进：

### 阶段 A：建立可编译环境
1. 验证 `dotnet --info`
2. 在 `D:\project\ErsatzTV` 执行 `dotnet restore`
3. 根据 CI 的做法，处理 `ErsatzTV.csproj` 中的 Scanner 引用问题
4. 执行 `dotnet build --configuration Release --no-restore`
5. 执行 `dotnet test --no-restore`

### 阶段 B：建立可发布环境
1. 执行 `dotnet publish`（win-x64）
2. 验证 `ErsatzTV.exe` 与 `ErsatzTV.Scanner.exe` 输出
3. 如有需要，再补 ffmpeg 与 launcher 组装流程

### 阶段 C：建立可改源码的工作流
1. 记录稳定可复现的构建命令
2. 做一个本地 BAT / PowerShell 构建脚本
3. 为后续源码修改准备回归验证流程

---

## 九、如果你要让我继续往下做，你需要提供/确认什么

### 最优先
请提供以下任一条件：

1. **允许我安装 .NET 10 SDK**
   - 可以是你本地已经放好的安装包路径
   - 也可以是允许我联网下载官方 SDK

或者：

2. **你自己先安装好 .NET 10 SDK 10.0.x**
   - 然后我继续后面的 restore / build / test / publish

### 最好再确认一下
1. 本机是否允许访问 NuGet
2. 目标是：
   - 只要“源码可编译环境”
   - 还是要“尽量接近官方 Win 发布包的完整构建环境”

---

## 十、当前结论（一句话版）

**源码仓库本身结构没看到明显致命问题，当前真正缺的是：本机没有 .NET 10 SDK。**

只要先补上 `.NET 10 SDK 10.0.x`，我就可以开始把这套 Win10 本地源码编译环境真正跑起来。
