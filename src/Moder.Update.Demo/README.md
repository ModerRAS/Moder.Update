# Moder.Update.Demo

A demo console application for testing the Moder.Update dual-process update flow.

## Prerequisites

- .NET 10.0 SDK (or later)
- Windows OS (the library uses Win32 `ReplaceFile` API)

## Quick Start

### Step 1: Build the solution

```bash
dotnet build src/Moder.Update.sln
```

### Step 2: Create a test update package

Create a package that updates FROM version 1.0.0 TO version 1.1.0:

```bash
dotnet run --project src/Moder.Update.Demo -- \
    --create-package 1.0.0 1.1.0 ./demo-app ./demo-packages
```

This creates:
- `demo-packages/catalog.json` - the update catalog
- `demo-packages/update-1.0.0-to-1.1.0.zst` - the update package

### Step 3: Publish the demo app

```bash
mkdir -p ./test-app
dotnet publish src/Moder.Update.Demo -c Release -o ./test-app
```

### Step 4: Verify update detection works

```bash
dotnet ./test-app/Moder.Update.Demo.dll --version
# Output: Current version: 1.0.0

dotnet ./test-app/Moder.Update.Demo.dll --check
# Output:
# Checking for updates from version 1.0.0...
# Packages directory: /path/to/demo-packages
# Update available! Latest version: 1.1.0
# Update path:
#   1.0.0 -> 1.1.0 (update-1.0.0-to-1.1.0.zst)
```

### Step 5: Apply the update

```bash
dotnet ./test-app/Moder.Update.Demo.dll --apply
```

This will:
1. Download the update package
2. Apply it to the application directory
3. Spawn the updater process and restart the app

After restart, run `--version` to confirm the update was applied.

## Commands

| Command | Description |
|---------|-------------|
| `--version` | Show current version |
| `--check` | Check for updates using local catalog |
| `--apply` | Apply update package and restart |
| `--create-package <from> <to> <source> <output>` | Create a test update package |

## How It Works

1. `--check` uses `UpdateChecker` to fetch `catalog.json` from the local `demo-packages/` directory
2. `--apply` downloads the `.zst` package and calls `UpdateManager.ApplyUpdateAsync()`
3. After applying, `PrepareRestart()` spawns `Moder.Update.Updater.exe` and the app exits
4. The updater waits for the app to exit, replaces files, verifies checksums, and restarts

## Package Format

Update packages use Moder.Update's binary format:
- 4 bytes magic: `MUP\0`
- Zstd-compressed tar archive containing:
  - `manifest.json` - version info, file list, checksums
  - Application files to replace

## Troubleshooting

**"Updater not found" warning**: The updater binary should be at `src/Moder.Update.Updater/bin/Release/net10.0-windows/win-x64/Moder.Update.Updater.exe`. Rebuild the updater if missing.

**"No update available"**: Make sure `demo-packages/catalog.json` exists and has entries with matching `minSourceVersion`.

**Package creation fails**: Ensure the source directory contains the files you want to package.
