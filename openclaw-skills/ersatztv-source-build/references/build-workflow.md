# ErsatzTV Source Build - Build Workflow

## Standard workflow for this machine

Use this order:

1. Verify `.NET 10 SDK` is available
2. Run restore
3. Run build
4. Run tests
5. If needed, run publish for `win-x64`
6. Save logs for every stage

## Important repository-specific rule

Before build/publish, temporarily remove this project reference from:

- `D:\project\ErsatzTV\ErsatzTV\ErsatzTV.csproj`

Reference to remove temporarily:

```xml
<ProjectReference Include="..\ErsatzTV.Scanner\ErsatzTV.Scanner.csproj" />
```

This mirrors the repository CI behavior.

Do **not** permanently hand-edit and leave the file changed. Prefer the script in `scripts/build_ersatztv.ps1`, which backs up and restores the file automatically.

## Restore commands

Basic restore:

```powershell
dotnet restore
```

Windows-targeted restore:

```powershell
dotnet restore -r win-x64
```

## Build command

```powershell
dotnet build --configuration Release --no-restore
```

## Test command

```powershell
dotnet test --blame-hang-timeout "2m" --no-restore --verbosity normal
```

## Publish pattern

Repository CI publishes both scanner and main app separately.

Typical Windows publish shape:

```powershell
dotnet publish ErsatzTV.Scanner\ErsatzTV.Scanner.csproj --framework net10.0 --runtime win-x64 -c Release -o publish\scanner -p:RestoreEnablePackagePruning=true -p:EnableCompressionInSingleFile=true -p:DebugType=Embedded -p:PublishSingleFile=true --self-contained true

dotnet publish ErsatzTV\ErsatzTV.csproj --framework net10.0 --runtime win-x64 -c Release -o publish\main -p:RestoreEnablePackagePruning=true -p:EnableCompressionInSingleFile=true -p:DebugType=Embedded -p:PublishSingleFile=true --self-contained true
```

## Logging

Prefer writing logs into:

- `D:\project\ErsatzTV\build-logs`

If a future build fails, preserve:

- restore log
- build log
- test log
- publish log
- any temporary project-file backup path used

## Offline goal note

The `.NET SDK` is already available offline.

The next likely network dependency is NuGet package restore. If the user later asks for a fully offline build workflow, the next step is to cache or mirror NuGet dependencies locally.
