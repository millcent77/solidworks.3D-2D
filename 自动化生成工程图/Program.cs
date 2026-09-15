using System.Collections.Concurrent;

namespace AutoDrawingMonitor;

internal static class Program
{
    private const string WatchPath = @"D:\AutoDrawing\Input";

    private static readonly string[] SupportedExtensions =
    [
        ".step",
        ".stp",
        ".sldprt"
    ];

    private static readonly ConcurrentDictionary<string, DateTime> PrintedFiles = new(StringComparer.OrdinalIgnoreCase);

    private static void Main()
    {
        Directory.CreateDirectory(WatchPath);

        Console.WriteLine($"Monitoring folder: {WatchPath}");
        Console.WriteLine("Supported files: STEP, STP, SLDPRT");
        Console.WriteLine("Press Ctrl+C to exit.");
        Console.WriteLine();

        PrintExistingFiles();

        using FileSystemWatcher watcher = new(WatchPath)
        {
            IncludeSubdirectories = false,
            Filter = "*.*",
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.CreationTime
                         | NotifyFilters.Size
        };

        watcher.Created += OnFileDetected;
        watcher.Changed += OnFileDetected;
        watcher.Renamed += OnFileRenamed;
        watcher.Error += OnWatcherError;
        watcher.EnableRaisingEvents = true;

        using ManualResetEventSlim exitSignal = new(false);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            exitSignal.Set();
        };

        exitSignal.Wait();
    }

    private static void PrintExistingFiles()
    {
        foreach (string filePath in Directory.EnumerateFiles(WatchPath))
        {
            TryPrintFileName(filePath);
        }
    }

    private static void OnFileDetected(object sender, FileSystemEventArgs eventArgs)
    {
        TryPrintFileName(eventArgs.FullPath);
    }

    private static void OnFileRenamed(object sender, RenamedEventArgs eventArgs)
    {
        TryPrintFileName(eventArgs.FullPath);
    }

    private static void TryPrintFileName(string filePath)
    {
        if (!IsSupportedFile(filePath))
        {
            return;
        }

        string fileName = Path.GetFileName(filePath);
        DateTime now = DateTime.UtcNow;

        // FileSystemWatcher can raise multiple events for one file. Keep output clean.
        if (PrintedFiles.TryGetValue(filePath, out DateTime lastPrinted)
            && now - lastPrinted < TimeSpan.FromSeconds(2))
        {
            return;
        }

        PrintedFiles[filePath] = now;
        Console.WriteLine(fileName);
    }

    private static bool IsSupportedFile(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static void OnWatcherError(object sender, ErrorEventArgs eventArgs)
    {
        Console.Error.WriteLine($"Watcher error: {eventArgs.GetException().Message}");
    }
}
