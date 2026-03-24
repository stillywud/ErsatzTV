# ErsatzTV Source Build - Offline Assets

## Current offline assets already preserved

### .NET SDK assets
Location:

- `D:\project\ErsatzTV\build-deps\dotnet-sdk`

Files include:

- `dotnet-sdk-10.0.201-win-x64.exe`
- `dotnet-sdk-10.0.201-win-x64.zip`
- `releases-10.0.json`
- `download-verification.json`
- install logs
- `dotnet-info-after-install.txt`

## Why this matters

Future agents should prefer these local files over re-downloading the SDK.

## Reinstall command

```powershell
& 'D:\project\ErsatzTV\build-deps\dotnet-sdk\dotnet-sdk-10.0.201-win-x64.exe' /install /quiet /norestart /log 'D:\project\ErsatzTV\build-deps\dotnet-sdk\reinstall-dotnet-sdk-10.0.201-win-x64.log'
```

## Integrity verification

Use the local verification file first:

- `D:\project\ErsatzTV\build-deps\dotnet-sdk\download-verification.json`

Or recompute hashes manually:

```powershell
Get-FileHash -Algorithm SHA512 'D:\project\ErsatzTV\build-deps\dotnet-sdk\dotnet-sdk-10.0.201-win-x64.exe'
Get-FileHash -Algorithm SHA512 'D:\project\ErsatzTV\build-deps\dotnet-sdk\dotnet-sdk-10.0.201-win-x64.zip'
```

## Not yet offline

As of 2026-03-24 / 2026-03-25, the following are not yet guaranteed offline:

- NuGet package restore for the repository
- external Windows packaging extras such as launcher/ffmpeg assets used by upstream CI

If the user later wants a fully offline build-and-package workflow, cache those next.
