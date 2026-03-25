using System.Diagnostics;
using Moder.Update.Compression;
using Moder.Update.FileOperations;

namespace Moder.Update.Updater;

/// <summary>
/// Standalone updater process that replaces application files and restarts the main application.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        try
        {
            var config = ParseArgs(args);
            return RunUpdate(config);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Moder.Update.Updater] Fatal error: {ex.Message}");
            return 1;
        }
    }

    private static int RunUpdate(UpdaterConfig config)
    {
        Console.WriteLine($"[Moder.Update.Updater] Waiting for process {config.TargetPid} to exit...");

        if (!WaitForProcessExit(config.TargetPid, config.WaitTimeout))
        {
            Console.Error.WriteLine($"[Moder.Update.Updater] Process {config.TargetPid} did not exit in time, killing...");
            KillProcess(config.TargetPid);
        }

        Console.WriteLine("[Moder.Update.Updater] Target process exited. Starting file replacement...");

        var fileService = new FileReplacementService();
        var replacedFiles = new List<string>();

        try
        {
            var targetDir = Path.GetDirectoryName(Path.GetFullPath(config.TargetPath))
                ?? throw new InvalidOperationException("Cannot determine target directory.");

            var stagingFiles = Directory.GetFiles(config.StagingDir, "*", SearchOption.AllDirectories);

            foreach (var stagingFile in stagingFiles)
            {
                var relativePath = Path.GetRelativePath(config.StagingDir, stagingFile);
                var targetPath = Path.Combine(targetDir, relativePath);

                Console.WriteLine($"  Replacing: {relativePath}");

                fileService.ReplaceFile(targetPath, stagingFile, config.BackupDir);
                replacedFiles.Add(relativePath);
            }

            Console.WriteLine($"[Moder.Update.Updater] Replaced {replacedFiles.Count} files.");

            CleanupDirectory(config.StagingDir);

            if (config.BackupDir is not null)
                fileService.Commit(config.BackupDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Moder.Update.Updater] Error during file replacement: {ex.Message}");

            if (config.BackupDir is not null && replacedFiles.Count > 0)
            {
                Console.WriteLine("[Moder.Update.Updater] Attempting rollback...");
                try
                {
                    var targetDir = Path.GetDirectoryName(Path.GetFullPath(config.TargetPath))!;
                    fileService.Rollback(replacedFiles, config.BackupDir, targetDir);
                    Console.WriteLine("[Moder.Update.Updater] Rollback completed.");
                }
                catch (Exception rollbackEx)
                {
                    Console.Error.WriteLine($"[Moder.Update.Updater] Rollback failed: {rollbackEx.Message}");
                }
            }

            RestartTarget(config);
            return 1;
        }

        RestartTarget(config);
        Console.WriteLine("[Moder.Update.Updater] Update complete.");
        return 0;
    }

    private static bool WaitForProcessExit(int pid, TimeSpan timeout)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            return process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static void KillProcess(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // Already exited
        }
    }

    private static void RestartTarget(UpdaterConfig config)
    {
        Console.WriteLine($"[Moder.Update.Updater] Restarting: {config.TargetPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = config.TargetPath,
            UseShellExecute = false,
        };

        if (config.RestartArgs is { Length: > 0 })
        {
            startInfo.Arguments = string.Join(' ', config.RestartArgs);
        }

        Process.Start(startInfo);
    }

    private static void CleanupDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private static UpdaterConfig ParseArgs(string[] args)
    {
        int? targetPid = null;
        string? targetPath = null;
        string? stagingDir = null;
        string? backupDir = null;
        int waitTimeoutSec = 30;
        string[]? restartArgs = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--target-pid" when i + 1 < args.Length:
                    targetPid = int.Parse(args[++i]);
                    break;
                case "--target-path" when i + 1 < args.Length:
                    targetPath = args[++i];
                    break;
                case "--staging-dir" when i + 1 < args.Length:
                    stagingDir = args[++i];
                    break;
                case "--backup-dir" when i + 1 < args.Length:
                    backupDir = args[++i];
                    break;
                case "--wait-timeout" when i + 1 < args.Length:
                    waitTimeoutSec = int.Parse(args[++i]);
                    break;
                case "--restart-args" when i + 1 < args.Length:
                    var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(args[++i]));
                    restartArgs = decoded.Split('\0');
                    break;
            }
        }

        return new UpdaterConfig
        {
            TargetPid = targetPid ?? throw new ArgumentException("--target-pid is required."),
            TargetPath = targetPath ?? throw new ArgumentException("--target-path is required."),
            StagingDir = stagingDir ?? throw new ArgumentException("--staging-dir is required."),
            BackupDir = backupDir,
            WaitTimeout = TimeSpan.FromSeconds(waitTimeoutSec),
            RestartArgs = restartArgs
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Moder.Update.Updater - Dual-process file updater");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Moder.Update.Updater --target-pid <pid> --target-path <path> --staging-dir <dir> [options]");
        Console.WriteLine();
        Console.WriteLine("Required:");
        Console.WriteLine("  --target-pid <pid>       PID of the main application process to wait for");
        Console.WriteLine("  --target-path <path>     Path to the main application executable");
        Console.WriteLine("  --staging-dir <dir>      Directory containing new files to replace");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --backup-dir <dir>       Directory for backing up original files (enables rollback)");
        Console.WriteLine("  --wait-timeout <sec>     Seconds to wait for target process exit (default: 30)");
        Console.WriteLine("  --restart-args <base64>  Base64-encoded restart arguments (null-separated)");
        Console.WriteLine("  --help, -h               Show this help message");
    }
}

internal class UpdaterConfig
{
    public required int TargetPid { get; init; }
    public required string TargetPath { get; init; }
    public required string StagingDir { get; init; }
    public string? BackupDir { get; init; }
    public TimeSpan WaitTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public string[]? RestartArgs { get; init; }
}
