---
name: ersatztv-source-build
description: Build, test, publish, or prepare source changes for the local ErsatzTV repository at `D:\project\ErsatzTV`. Use when working on this specific project for tasks like restoring NuGet packages, compiling `net10.0` code, validating the Windows 10 build environment, updating or patching ErsatzTV source, or preparing repeatable local build workflows. Also use when a future agent needs the known local facts for this repo: .NET SDK 10.0.201 is already installed, offline SDK assets are stored under `D:\project\ErsatzTV\build-deps\dotnet-sdk`, and the repo CI temporarily removes the `ErsatzTV.Scanner` project reference from `ErsatzTV\ErsatzTV.csproj` before build.
---

# ErsatzTV Source Build

## Overview

Use this skill to continue maintaining the local ErsatzTV source tree on this Windows 10 machine without rediscovering the build environment from scratch.

Prefer the supplied script for build/test/publish work so the temporary `Scanner` project-reference workaround is applied and then restored automatically.

## Workflow

### 1. Read the local project context first

Read these references before making build decisions:

- `references/project-context.md`
- `references/build-workflow.md`
- `references/offline-assets.md`

If you need the full human-facing repo docs, also read:

- `D:\project\ErsatzTV\OPENCLAW_ERSATZTV_MAINTAINER_GUIDE.md`
- `D:\project\ErsatzTV\OPENCLAW_BUILD_PREP_2026-03-24.md`
- `D:\project\ErsatzTV\OPENCLAW_DOTNET10_INSTALL_2026-03-24.md`

### 2. Verify .NET before building

Run:

```powershell
dotnet --info
```

Expect:

- SDK `10.0.x`
- currently installed SDK is `10.0.201`

If `dotnet` is missing, do not guess. Reuse the offline installer described in `references/offline-assets.md`.

### 3. Prefer the scripted build path

Use:

- `scripts/build_ersatztv.ps1`

This script:

- verifies the repo layout
- writes logs under `build-logs/`
- backs up `ErsatzTV\ErsatzTV.csproj`
- temporarily removes the `ErsatzTV.Scanner` project reference
- runs restore/build/test
- optionally runs `publish win-x64`
- restores the original csproj afterward

### 4. Use command patterns like these

Plan only:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\intel1230\skills\ersatztv-source-build\scripts\build_ersatztv.ps1 -PlanOnly
```

Standard local build:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\intel1230\skills\ersatztv-source-build\scripts\build_ersatztv.ps1
```

Restore/build without tests:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\intel1230\skills\ersatztv-source-build\scripts\build_ersatztv.ps1 -SkipTests
```

Windows publish flow:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\intel1230\skills\ersatztv-source-build\scripts\build_ersatztv.ps1 -Runtime win-x64 -PublishWinX64
```

### 5. Preserve reproducibility

When you change the local build setup:

- update the repo docs under `D:\project\ErsatzTV`
- update this skill if the workflow changed
- preserve downloaded installers or other offline assets under a stable project-local directory
- avoid one-off terminal-only knowledge

## Guardrails

- Do not permanently leave `ErsatzTV\ErsatzTV.csproj` modified just to make a build pass.
- Do not re-download the .NET SDK if the local offline copy is still valid.
- Prefer log files over transient terminal output.
- Treat NuGet restore as the next likely online dependency; if the user later asks for a fully offline build, cache that separately instead of pretending the current workflow is fully offline.

## Resources

### scripts/

- `scripts/build_ersatztv.ps1` — deterministic local build/test/publish helper for this repo

### references/

- `references/project-context.md` — repo path, environment facts, installed SDK, local docs
- `references/build-workflow.md` — exact build sequence and project-specific caveats
- `references/offline-assets.md` — local offline SDK assets and reinstall instructions
