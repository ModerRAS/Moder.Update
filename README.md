# Moder.Update

A .NET dual-process self-update library. Applications can download updates, spawn a detached updater process, exit, have the updater replace files atomically, and restart.

## Features

- **Dual-process update pattern** — main app downloads update, spawns updater, exits; updater replaces files and restarts main app
- **Zstd compression** — update packages use Zstd (via ZstdSharp) for efficient compression
- **Atomic file replacement** — uses `ReplaceFile` Win32 API on Windows for reliable file replacement
- **Rollback support** — automatic backup and rollback on failure
- **Chain update path** — supports version chains with anchor and cumulative packages
- **SHA512 verification** — file-level integrity checking
- **No built-in HTTP** — consumers provide their own HTTP implementation via `IUpdateCatalogFetcher`
- **Headless** — pure library with event-based progress reporting, no UI

## Getting Started

### Install

```bash
dotnet add package Moder.Update
```

### Basic Usage

```csharp
using Moder.Update;
using Moder.Update.Compression;
using Moder.Update.FileOperations;
using Moder.Update.Models;
using Moder.Update.Package;

// Set up components
var compressor = new ZstdCompressor();
var packageReader = new ZstdPackageReader(compressor);
var fileService = new FileReplacementService();
var processSpawner = new ProcessSpawner();
var updateManager = new UpdateManager(packageReader, fileService, processSpawner);

// Check for updates (implement IUpdateCatalogFetcher with your HTTP client)
var checker = new UpdateChecker(myFetcher, updateManager);
var result = await checker.CheckForUpdatesAsync("1.0.0");

if (result.Status == UpdateCheckStatus.UpdateAvailable)
{
    foreach (var entry in result.UpdatePath!)
    {
        // Download and apply each package
        using var packageStream = await myFetcher.DownloadPackageAsync(entry.PackagePath);
        var updateResult = await updateManager.ApplyUpdateAsync(packageStream, new UpdateOptions
        {
            CurrentVersion = "1.0.0",
            TargetDir = AppContext.BaseDirectory,
            EnableRollback = true
        });
    }

    // Spawn updater and exit
    updateManager.PrepareRestart("Moder.Update.Updater.exe", options);
    Environment.Exit(0);
}
```

### Implementing IUpdateCatalogFetcher

```csharp
public class MyHttpFetcher : IUpdateCatalogFetcher
{
    private readonly HttpClient _client;
    private readonly string _baseUrl;

    public MyHttpFetcher(HttpClient client, string baseUrl)
    {
        _client = client;
        _baseUrl = baseUrl;
    }

    public async Task<string> FetchCatalogAsync(CancellationToken ct = default)
    {
        return await _client.GetStringAsync($"{_baseUrl}/catalog.json", ct);
    }

    public async Task<Stream> DownloadPackageAsync(string packagePath, CancellationToken ct = default)
    {
        return await _client.GetStreamAsync($"{_baseUrl}/{packagePath}", ct);
    }
}
```

## Package Format

Update packages use the following binary format:

```
[4 bytes: "MUP\0" magic] + [Zstd-compressed tar archive]
```

The tar archive contains:
- `manifest.json` — update manifest with version info, file list, and checksums
- Application files — full replacement files (not diffs)

## Update Catalog

The update catalog is a JSON file served at a fixed URL:

```json
{
  "latestVersion": "2.0.0",
  "minRequiredVersion": "1.0.0",
  "lastUpdated": "2025-01-01T00:00:00Z",
  "entries": [
    {
      "packagePath": "packages/1.1.0.zst",
      "targetVersion": "1.1.0",
      "minSourceVersion": "1.0.0",
      "maxSourceVersion": "1.0.99",
      "packageChecksum": "sha512hash...",
      "fileCount": 5,
      "compressedSize": 1024000,
      "uncompressedSize": 2048000
    }
  ]
}
```

## Updater Process

The `Moder.Update.Updater` is a standalone console application that:

1. Waits for the main application process to exit
2. Replaces application files using atomic operations
3. Restarts the main application

```bash
Moder.Update.Updater --target-pid 1234 --target-path /path/to/app --staging-dir /path/to/staging
```

## Building

```bash
dotnet build src/Moder.Update.sln
dotnet test src/Moder.Update.sln
```

## Packing

```bash
./scripts/pack.sh   # Linux/macOS
scripts\pack.cmd     # Windows
```

## Demo

A demo project (`Moder.Update.Demo`) is included to test the update flow end-to-end.

### Prerequisites

- .NET 10.0 SDK (or later)
- Windows OS (the library uses Win32 `ReplaceFile` API)

### Quick Start

```bash
# 1. Build the solution
dotnet build src/Moder.Update.sln

# 2. Create a test package (1.0.0 -> 1.1.0)
dotnet run --project src/Moder.Update.Demo -- \
    --create-package 1.0.0 1.1.0 ./demo-app ./demo-packages

# 3. Copy demo app to a test directory
mkdir -p ./test-app
dotnet publish src/Moder.Update.Demo -c Release -o ./test-app

# 4. Run the app with --check to see update status
dotnet run --project ./test-app/Moder.Update.Demo.dll -- --check

# 5. Run with --apply to apply the update and restart
dotnet run --project ./test-app/Moder.Update.Demo.dll -- --apply
```

### Demo Commands

| Command | Description |
|---------|-------------|
| `--version` | Show current version |
| `--check` | Check for updates using local catalog |
| `--apply` | Download and apply update package, then restart |
| `--create-package <from> <to> <source> <output>` | Create a test update package |

### Creating Update Packages

```bash
# Linux/macOS
./scripts/create-demo-package.sh 1.0.0 1.1.0 ./my-app ./demo-packages

# Windows
scripts\create-demo-package.cmd 1.0.0 1.1.0 .\my-app .\demo-packages
```

### Demo Package Location

The demo looks for update packages in `../../../demo-packages` relative to the demo binary. This directory should contain:
- `catalog.json` - the update catalog
- `*.zst` - the update packages

## License

MIT