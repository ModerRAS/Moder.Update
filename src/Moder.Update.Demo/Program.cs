using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moder.Update;
using Moder.Update.Compression;
using Moder.Update.Demo.Helpers;
using Moder.Update.FileOperations;
using Moder.Update.Models;
using Moder.Update.Package;

namespace Moder.Update.Demo;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "--help";
        var rest = args.Length > 1 ? args[1..] : [];

        return command switch
        {
            "--help" => ShowHelp(),
            "--version" => await ShowVersionAsync(),
            "--check" => await CheckForUpdatesAsync(),
            "--apply" => await ApplyUpdateAsync(),
            "--create-package" => CreatePackageAsync(rest).GetAwaiter().GetResult(),
            _ => ShowHelp()
        };
    }

    private static int ShowHelp()
    {
        Console.WriteLine("Moder.Update Demo Application");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  --help                       Show this help message");
        Console.WriteLine("  --version                    Show current version");
        Console.WriteLine("  --check                      Check for updates");
        Console.WriteLine("  --apply                      Apply update and restart");
        Console.WriteLine("  --create-package <fromVer> <toVer> <sourceDir> <outputDir>  Create a test package");
        Console.WriteLine();
        return 0;
    }

    private static async Task<int> ShowVersionAsync()
    {
        var demoDir = AppContext.BaseDirectory;
        var versionManager = new DemoVersionManager(demoDir);
        versionManager.InitializeIfNeeded("1.0.0");
        var version = versionManager.GetCurrentVersion();
        Console.WriteLine($"Current version: {version}");
        return 0;
    }

    private static async Task<int> CheckForUpdatesAsync()
    {
        var demoDir = AppContext.BaseDirectory;
        var packagesDir = Path.GetFullPath(Path.Combine(demoDir, "../../../demo-packages"));
        var updaterPath = Path.GetFullPath(Path.Combine(demoDir, "../../../src/Moder.Update.Updater/bin/Release/net10.0-windows/win-x64/Moder.Update.Updater.exe"));

        var versionManager = new DemoVersionManager(demoDir);
        versionManager.InitializeIfNeeded("1.0.0");
        var currentVersion = versionManager.GetCurrentVersion();

        if (!File.Exists(updaterPath))
        {
            Console.WriteLine($"Updater not found at: {updaterPath}");
            Console.WriteLine("Update functionality will be limited.");
        }

        var compressor = new ZstdCompressor();
        var packageReader = new ZstdPackageReader(compressor);
        var fileService = new FileReplacementService();
        var processSpawner = new ProcessSpawner();
        var manager = new UpdateManager(packageReader, fileService, processSpawner);
        var fetcher = new LocalUpdateCatalogFetcher(packagesDir);
        var checker = new UpdateChecker(fetcher, manager);

        Console.WriteLine($"Checking for updates from version {currentVersion}...");
        Console.WriteLine($"Packages directory: {packagesDir}");

        try
        {
            var result = await checker.CheckForUpdatesAsync(currentVersion);

            switch (result.Status)
            {
                case UpdateCheckStatus.UpToDate:
                    Console.WriteLine("You are up to date!");
                    break;
                case UpdateCheckStatus.UpdateAvailable:
                    Console.WriteLine($"Update available! Latest version: {result.LatestVersion}");
                    Console.WriteLine("Update path:");
                    foreach (var entry in result.UpdatePath!)
                    {
                        Console.WriteLine($"  {entry.MinSourceVersion} -> {entry.TargetVersion} ({entry.PackagePath})");
                    }
                    break;
                case UpdateCheckStatus.UpdateUnavailable:
                    Console.WriteLine($"Update unavailable: {result.Message}");
                    break;
                case UpdateCheckStatus.NoPathFound:
                    Console.WriteLine($"No update path found: {result.Message}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking for updates: {ex.Message}");
            return 1;
        }

        return 0;
    }

    private static async Task<int> ApplyUpdateAsync()
    {
        var demoDir = AppContext.BaseDirectory;
        var packagesDir = Path.GetFullPath(Path.Combine(demoDir, "../../../demo-packages"));
        var updaterPath = Path.GetFullPath(Path.Combine(demoDir, "../../../src/Moder.Update.Updater/bin/Release/net10.0-windows/win-x64/Moder.Update.Updater.exe"));

        if (!File.Exists(updaterPath))
        {
            Console.WriteLine($"Updater not found at: {updaterPath}");
            Console.WriteLine("Cannot apply update without the updater.");
            return 1;
        }

        var versionManager = new DemoVersionManager(demoDir);
        versionManager.InitializeIfNeeded("1.0.0");
        var currentVersion = versionManager.GetCurrentVersion();

        var compressor = new ZstdCompressor();
        var packageReader = new ZstdPackageReader(compressor);
        var fileService = new FileReplacementService();
        var processSpawner = new ProcessSpawner();
        var manager = new UpdateManager(packageReader, fileService, processSpawner);
        var fetcher = new LocalUpdateCatalogFetcher(packagesDir);
        var checker = new UpdateChecker(fetcher, manager);

        Console.WriteLine($"Applying update from version {currentVersion}...");

        var result = await checker.CheckForUpdatesAsync(currentVersion);

        if (result.Status != UpdateCheckStatus.UpdateAvailable || result.UpdatePath is null || result.UpdatePath.Count == 0)
        {
            Console.WriteLine("No update available.");
            return 1;
        }

        var targetDir = demoDir;
        var stagingDir = Path.Combine(targetDir, ".update-staging");
        var backupDir = Path.Combine(targetDir, ".update-backup");

        foreach (var entry in result.UpdatePath)
        {
            Console.WriteLine($"Downloading package: {entry.PackagePath}...");
            await using var packageStream = await fetcher.DownloadPackageAsync(entry.PackagePath);

            Console.WriteLine("Applying update...");
            var options = new UpdateOptions
            {
                CurrentVersion = currentVersion,
                TargetDir = targetDir,
                EnableRollback = true,
                BackupDir = backupDir,
                StagingDir = stagingDir
            };

            var updateResult = await manager.ApplyUpdateAsync(packageStream, options);

            if (!updateResult.Success)
            {
                Console.WriteLine($"Update failed: {updateResult.ErrorMessage}");
                if (updateResult.RollbackPerformed)
                    Console.WriteLine("Rollback was performed.");
                return 1;
            }

            Console.WriteLine($"Update applied! {updateResult.FilesUpdated} files updated.");

            if (updateResult.NextTargetVersion is not null)
                currentVersion = updateResult.NextTargetVersion;
        }

        Console.WriteLine("Preparing restart...");
        var finalOptions = new UpdateOptions
        {
            CurrentVersion = currentVersion,
            TargetDir = targetDir,
            EnableRollback = true,
            BackupDir = backupDir,
            StagingDir = stagingDir
        };

        manager.PrepareRestart(updaterPath, finalOptions);
        Console.WriteLine("Restarting...");
        Environment.Exit(0);
        return 0;
    }

    private static async Task<int> CreatePackageAsync(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("Error: --create-package requires <fromVer> <toVer> <sourceDir> <outputDir>");
            Console.WriteLine("Usage: --create-package <fromVer> <toVer> <sourceDir> <outputDir>");
            return 1;
        }

        var fromVer = args[0];
        var toVer = args[1];
        var sourceDir = Path.GetFullPath(args[2]);
        var outputDir = Path.GetFullPath(args[3]);

        if (!Directory.Exists(sourceDir))
        {
            Console.WriteLine($"Error: Source directory not found: {sourceDir}");
            return 1;
        }

        Directory.CreateDirectory(outputDir);

        var demoDir = AppContext.BaseDirectory;
        var builder = new TestPackageBuilder(new ZstdCompressor());

        var files = new Dictionary<string, byte[]>();
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            files[relativePath] = File.ReadAllBytes(file);
        }

        var demoProgramPath = Path.Combine(demoDir, "Moder.Update.Demo.dll");
        if (File.Exists(demoProgramPath))
            files["Moder.Update.Demo.dll"] = File.ReadAllBytes(demoProgramPath);

        var demoExePath = Path.Combine(demoDir, "Moder.Update.Demo.exe");
        if (File.Exists(demoExePath))
            files["Moder.Update.Demo.exe"] = File.ReadAllBytes(demoExePath);

        var versionTxtPath = Path.Combine(demoDir, "version.txt");
        if (File.Exists(versionTxtPath))
            files["version.txt"] = File.ReadAllBytes(versionTxtPath);

        Console.WriteLine($"Creating package from {fromVer} to {toVer} with {files.Count} files...");

        var packagePath = await builder.CreatePackageAsync(toVer, fromVer, null, files, outputDir);

        Console.WriteLine($"Package created: {packagePath}");
        return 0;
    }

    private static string ComputeSha512String(byte[] data)
    {
        var hash = SHA512.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
