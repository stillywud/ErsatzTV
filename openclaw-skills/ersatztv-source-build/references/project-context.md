# ErsatzTV Source Build - Project Context

## Repository

- Repo root: `D:\project\ErsatzTV`
- Primary solution: `D:\project\ErsatzTV\ErsatzTV.sln`

## Why this skill exists

This repository is being maintained locally on Windows 10 with the goal of making source edits, rebuilding, testing, and eventually updating the application despite the upstream project no longer being actively updated.

This skill exists so future agents do not need to rediscover:

- the required .NET version
- the current local machine setup
- the CI build pattern
- the known `Scanner` project-reference workaround
- the local offline dependency stash

## Confirmed local setup

### Installed .NET SDK
- Installed SDK: `10.0.201`
- Installed by OpenClaw on 2026-03-24
- Verification snapshot saved at:
  - `D:\project\ErsatzTV\build-deps\dotnet-sdk\dotnet-info-after-install.txt`

### Offline .NET assets
Stored in:

- `D:\project\ErsatzTV\build-deps\dotnet-sdk`

Includes:

- `dotnet-sdk-10.0.201-win-x64.exe`
- `dotnet-sdk-10.0.201-win-x64.zip`
- `releases-10.0.json`
- `download-verification.json`
- install logs

## Required framework

Read these files if needed:

- `D:\project\ErsatzTV\global.json`
- `D:\project\ErsatzTV\ErsatzTV\ErsatzTV.csproj`
- `D:\project\ErsatzTV\ErsatzTV.Scanner\ErsatzTV.Scanner.csproj`

Current requirement:

- `.NET SDK 10.0.x`
- project targets `net10.0`

## Build-system clue from CI

The repo CI uses .NET 10 and, before build, strips the `Scanner` project reference from `ErsatzTV\ErsatzTV.csproj`.

That means future agents should not assume a plain `dotnet build` on the untouched project file is the intended path.

Instead, use the supplied build script in this skill or mirror the CI workflow carefully.

## Local project docs worth reading

- `D:\project\ErsatzTV\OPENCLAW_ERSATZTV_MAINTAINER_GUIDE.md`
- `D:\project\ErsatzTV\OPENCLAW_BUILD_PREP_2026-03-24.md`
- `D:\project\ErsatzTV\OPENCLAW_DOTNET10_INSTALL_2026-03-24.md`
