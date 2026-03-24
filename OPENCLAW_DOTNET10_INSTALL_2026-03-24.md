# .NET 10 SDK 离线安装记录（OpenClaw，2026-03-24）

## 目标

在当前 Win10 机器上为 `D:\project\ErsatzTV` 建立可复现的本地源码编译环境，并且：

- 所有关键步骤形成文档
- 下载的安装包保留在本地
- 后续尽量不再依赖网络重新下载安装资源

---

## 一、安装前确认

### 项目要求
从仓库文件确认：

- `D:\project\ErsatzTV\global.json`
- `D:\project\ErsatzTV\ErsatzTV\ErsatzTV.csproj`
- `D:\project\ErsatzTV\ErsatzTV.Scanner\ErsatzTV.Scanner.csproj`

结论：

- 项目需要 **.NET 10 SDK**
- 目标框架为 **`net10.0`**

### 安装前机器状态
安装前检查结果：

- `git`：已安装
- `dotnet`：**未安装**
- `7z`：未安装
- `msbuild`：未安装
- `nuget`：未安装

说明：

- 当前真正阻塞源码编译的，是 **缺少 .NET 10 SDK**
- `7z` / `msbuild` / `nuget.exe` 不是第一步阻塞项

---

## 二、下载并保留的离线资产

统一保存目录：

- `D:\project\ErsatzTV\build-deps\dotnet-sdk`

已保留文件：

1. 官方 release metadata
   - `releases-10.0.json`
2. SDK 安装器（Windows x64）
   - `dotnet-sdk-10.0.201-win-x64.exe`
3. SDK 压缩包（Windows x64）
   - `dotnet-sdk-10.0.201-win-x64.zip`
4. 安装日志
   - `install-dotnet-sdk-10.0.201-win-x64.log`
   - 以及各 MSI 子日志
5. 安装后信息快照
   - `dotnet-info-after-install.txt`
6. 下载校验结果
   - `download-verification.json`

---

## 三、下载来源（官方）

### 1. release metadata
URL：

- `https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json`

用途：

- 记录 .NET 10 频道最新 SDK / Runtime 版本
- 提供官方文件下载 URL 与哈希

### 2. SDK 安装器（官方 EXE）
URL：

- `https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.201/dotnet-sdk-10.0.201-win-x64.exe`

### 3. SDK 压缩包（官方 ZIP）
URL：

- `https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.201/dotnet-sdk-10.0.201-win-x64.zip`

说明：

- EXE 用于标准 Windows 安装
- ZIP 用于离线归档 / 特殊情况下的手动部署参考

---

## 四、下载方式

### 实际验证结果
在这台机器上：

- PowerShell `Invoke-WebRequest` 下载大文件时表现不稳定
- 最终改用 `curl.exe` 成功完成下载

### 实际下载命令
```powershell
$root = 'D:\project\ErsatzTV\build-deps\dotnet-sdk'
New-Item -ItemType Directory -Force -Path $root | Out-Null

& 'C:\Windows\System32\curl.exe' -L --fail --output (Join-Path $root 'dotnet-sdk-10.0.201-win-x64.exe') 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.201/dotnet-sdk-10.0.201-win-x64.exe'
& 'C:\Windows\System32\curl.exe' -L --fail --output (Join-Path $root 'dotnet-sdk-10.0.201-win-x64.zip') 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.201/dotnet-sdk-10.0.201-win-x64.zip'
```

如需以后重下，优先使用这个方式。

---

## 五、文件校验

校验算法：

- **SHA512**

### 官方哈希（来自 release metadata）

#### EXE
- 文件：`dotnet-sdk-10.0.201-win-x64.exe`
- SHA512：

```text
4f51c56e4e9976266f8f6a44e40583bf942955a192a88ceb482fba7975a1eb8f049ae2dc6265c0b3972f1e1fea7237cc96fea46c38c0fa9c29b0b51e6aa414e4
```

#### ZIP
- 文件：`dotnet-sdk-10.0.201-win-x64.zip`
- SHA512：

```text
15f511296c0ea28a61bafcc2efbc66cd4d22d15a2db44597b9b2254a9eafb57047bbe2feca8106a17ba7ae8af19a3a6847a375b6740e0bf6ab0874ade8af0bca
```

### 实际校验命令
```powershell
Get-FileHash -Algorithm SHA512 'D:\project\ErsatzTV\build-deps\dotnet-sdk\dotnet-sdk-10.0.201-win-x64.exe'
Get-FileHash -Algorithm SHA512 'D:\project\ErsatzTV\build-deps\dotnet-sdk\dotnet-sdk-10.0.201-win-x64.zip'
```

### 实际结果
- EXE：**匹配官方哈希**
- ZIP：**匹配官方哈希**

结果已保存到：

- `D:\project\ErsatzTV\build-deps\dotnet-sdk\download-verification.json`

---

## 六、安装方式

### 实际安装命令
```powershell
& 'D:\project\ErsatzTV\build-deps\dotnet-sdk\dotnet-sdk-10.0.201-win-x64.exe' /install /quiet /norestart /log 'D:\project\ErsatzTV\build-deps\dotnet-sdk\install-dotnet-sdk-10.0.201-win-x64.log'
```

### 说明
- `/install`：执行安装
- `/quiet`：静默安装
- `/norestart`：不自动重启
- `/log`：将主安装日志写到指定文件

### 一个重要现象
OpenClaw 当前会话本身**不能直接请求提权工具通道**，但该安装器在实际运行时：

- 成功启动了自己的 elevated engine
- 并完成了 MSI 链式安装

也就是说：

- **本次安装是成功的**
- 且日志中可以看到安装器自行拉起提升阶段

日志关键线索类似：

```text
i010: Launching elevated engine process.
i011: Launched elevated engine process.
i012: Connected to elevated engine.
```

---

## 七、安装日志位置

### 主日志
- `D:\project\ErsatzTV\build-deps\dotnet-sdk\install-dotnet-sdk-10.0.201-win-x64.log`

### 子日志
同目录下还保留了每个 MSI 的详细日志，例如：

- `install-dotnet-sdk-10.0.201-win-x64_001_dotnet_hostfxr_10.0.5_win_x64.msi.log`
- `install-dotnet-sdk-10.0.201-win-x64_003_dotnet_runtime_10.0.5_win_x64.msi.log`
- `install-dotnet-sdk-10.0.201-win-x64_011_dotnet_sdk_internal_10.0.201_servicing.26153.122_win_x64.msi.log`

这些日志后续都可以用于排查离线复装问题。

---

## 八、安装结果验证

### 验证命令
```powershell
$env:PATH = [System.Environment]::GetEnvironmentVariable('PATH','Machine') + ';' + [System.Environment]::GetEnvironmentVariable('PATH','User')
dotnet --info
```

### 实际结果
已成功安装：

- **.NET SDK 10.0.201**
- Host 版本：**10.0.5**

已安装运行时：

- `Microsoft.AspNetCore.App 10.0.5`
- `Microsoft.NETCore.App 10.0.5`
- `Microsoft.WindowsDesktop.App 10.0.5`

安装后 `dotnet --info` 输出已保存到：

- `D:\project\ErsatzTV\build-deps\dotnet-sdk\dotnet-info-after-install.txt`

---

## 九、离线复装建议

如果以后换机或系统重装，希望不再依赖网络，建议保留整个目录：

- `D:\project\ErsatzTV\build-deps\dotnet-sdk`

最少要保留：

1. `dotnet-sdk-10.0.201-win-x64.exe`
2. `dotnet-sdk-10.0.201-win-x64.zip`
3. `releases-10.0.json`
4. `download-verification.json`
5. 本文档

### 离线重装命令
```powershell
& 'D:\project\ErsatzTV\build-deps\dotnet-sdk\dotnet-sdk-10.0.201-win-x64.exe' /install /quiet /norestart /log 'D:\project\ErsatzTV\build-deps\dotnet-sdk\reinstall-dotnet-sdk-10.0.201-win-x64.log'
```

### 重装后验证
```powershell
dotnet --info
```

---

## 十、对 ErsatzTV 项目的意义

现在 `.NET 10 SDK` 已经到位，原先最大的构建阻塞项已经解除。

接下来可以推进：

1. `dotnet restore`
2. `dotnet build`
3. `dotnet test`
4. `dotnet publish`（如需要）

也就是说：

> **当前机器已经具备建立 ErsatzTV 本地源码可编译环境的核心前提。**

---

## 十一、下一步建议

下一步建议按这个顺序执行：

1. 在 `D:\project\ErsatzTV` 执行 `dotnet restore`
2. 根据 CI 的做法处理 `ErsatzTV.csproj` 中 Scanner 引用细节
3. 执行 `dotnet build --configuration Release`
4. 执行 `dotnet test`
5. 记录第一次完整构建过程
6. 再决定是否继续做 Windows 打包与 launcher/ffmpeg 组装

---

## 十二、一句话结论

**.NET 10 SDK 10.0.201 已经成功安装，离线安装包、哈希、日志、验证结果都已保存在 `D:\project\ErsatzTV\build-deps\dotnet-sdk`，后续可以不依赖网络重复安装。**
