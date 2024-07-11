using FileSyncer.Cli.Extensions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;

namespace FileSyncer.Cli
{
    internal class Program
    {
        private static string? _logPath;
        private static Stream? _logStream;

        private static string[]? _ignore;
        private static string[]? _include;

        private static bool _dryRun;

        private static long _synced { get; set; }
        private static long _toSync { get; set; }
        private static long _removed { get; set; }
        private static long _toRemove { get; set; }
        private static long _errors { get; set; }

        private static long _requiredTargetFreeSpace { get; set; }

        static void Main(string[] args)
        {
            var stopwatch = new Stopwatch();

            try
            {
                var argsDict = args.GetArgumets();

                string sourcePath = GetFromArgsOrPrompt(argsDict, "Source");
                string targetPath = GetFromArgsOrPrompt(argsDict, "Target");
                _include = GetFromArgsOrPrompt(argsDict, "Include", false)?.Split(",", StringSplitOptions.RemoveEmptyEntries);
                _ignore = GetFromArgsOrPrompt(argsDict, "Ignore", false)?.Split(",", StringSplitOptions.RemoveEmptyEntries);
                _logPath = GetFromArgsOrPrompt(argsDict, "LogFile", false);

                _dryRun = argsDict.ContainsKey("DryRun");
                bool copyAcl = argsDict.ContainsKey("CopyAcl");
                bool copyDates = argsDict.ContainsKey("CopyDates");

                ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath, nameof(sourcePath));
                ArgumentException.ThrowIfNullOrWhiteSpace(targetPath, nameof(targetPath));

                Console.WriteLine();

                Log($"Starting sync {DateTime.Now} Source: {sourcePath} Target: {targetPath}" +
                      $"{Environment.NewLine} - DruRun: {_dryRun}"
                    + $"{Environment.NewLine} - Include: {((_include?.Any() ?? false) ? string.Join(",", _include) : "ALL")}"
                    + $"{Environment.NewLine} - Ignore: {((_ignore?.Any() ?? false) ? string.Join(",", _ignore) : "NONE")}");
                Console.WriteLine();

                ResetCounters();

                stopwatch.Start();

                Log("Removing files that are not present on source but found at target");
                RemoveOrphanFiles(sourcePath, targetPath, _dryRun);

                Console.WriteLine();

                Log("Syncing files from source to target");
                SyncFiles(sourcePath, targetPath, _dryRun, copyAcl, copyDates);
            }
            catch (Exception ex)
            {
                if (_errors < 1) _errors = 1;
                Log(ex);
            }
            finally
            {
                stopwatch.Stop();

                Console.WriteLine();
                Log($"Operation completed. Synced: {_synced} Removed from target: {_removed} Errors: {_errors}. Elapsed: {stopwatch.Elapsed}");

                _logStream?.Dispose();
            }
        }

        private static void ResetCounters()
        {
            _synced = 0;
            _toSync = 0;
            _removed = 0;
            _toRemove = 0;
            _errors = 0;
            _requiredTargetFreeSpace = 0;
        }

        static void Log(string message, string? fileLogMessage = null)
        {
            fileLogMessage ??= message;

            if (!string.IsNullOrEmpty(_logPath))
            {
                if (_logStream == null) _logStream = File.Open(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                if (_logStream?.CanWrite ?? false)
                {
                    var buffer = Encoding.UTF8.GetBytes(fileLogMessage + Environment.NewLine);
                    _logStream.Write(buffer);
                    _logStream.Flush();
                }
            }

            Console.WriteLine(message);
        }

        static void Log(Exception ex) => Log($"Error: {ex.Message}", ex.ToString());

        static long GetDirectoryFreeSpace(string directoryPath, out string textRepresentation)
        {
            string root = Path.GetPathRoot(directoryPath) ?? throw new ArgumentException("Invalid directory path", nameof(directoryPath));

            DriveInfo drive = new DriveInfo(root);

            textRepresentation = FormatBytes(drive.AvailableFreeSpace);

            return drive.AvailableFreeSpace;
        }

        static string FormatBytes(long bytes)
        {
            const int scale = 1024;
            string[] orders = new string[] { "TB", "GB", "MB", "KB", "B" };
            long max = (long)Math.Pow(scale, orders.Length - 1);

            foreach (string order in orders)
            {
                if (bytes > max)
                    return string.Format("{0:##.##} {1}", decimal.Divide(bytes, max), order);

                max /= scale;
            }
            return "0 Bytes";
        }

        static string GetFromArgsOrPrompt(Dictionary<string, string> argsDict, string key, bool required = true)
        {
            if (argsDict.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            if (required)
            {
                Console.WriteLine($"{key}: ");
                value = Console.ReadLine();
            }

            return value ?? string.Empty;
        }

        static bool IsIgnored(string path)
        {
            if (!(_ignore?.Any() ?? false)) return false;

            return path.Replace("\\", "/").Split("/", StringSplitOptions.RemoveEmptyEntries).Any(p => _ignore.Contains(p));
        }

        static bool IsIncluded(string path)
        {
            if (!(_include?.Any() ?? false)) return true;

            return path.Replace("\\", "/").Split("/", StringSplitOptions.RemoveEmptyEntries).Any(p => _include.Contains(p));
        }

        static void SyncFiles(string sourcePath, string targetPath, bool dryRun, bool copyAcl, bool copyDates)
        {
            var toSync = new List<string>();

            Log("Calculating files to sync...");

            foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
                                          .Where(file => IsIncluded(file) && !IsIgnored(file)))
            {
                var sourceFileInfo = new FileInfo(file);
                var targetFilePath = file.Replace(sourcePath, targetPath);
                var targetFileInfo = new FileInfo(targetFilePath);

                if (!targetFileInfo.Exists || (sourceFileInfo.LastWriteTimeUtc != targetFileInfo.LastWriteTimeUtc && copyDates) || (targetFileInfo.Length != sourceFileInfo.Length))
                {
                    _requiredTargetFreeSpace += sourceFileInfo.Length;
                    toSync.Add(file);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && copyAcl && !CompareAcl(sourceFileInfo, targetFileInfo))
                {
                    _requiredTargetFreeSpace += sourceFileInfo.Length;
                    toSync.Add(file);
                }
            }

            _toSync = toSync.Count;
            var freeSpace = GetDirectoryFreeSpace(targetPath, out string freeSpaceText);

            Log($"Required storage space: {FormatBytes(_requiredTargetFreeSpace)} Free target space: {freeSpaceText}");

            if (_requiredTargetFreeSpace > freeSpace)
                throw new IOException($"Target directory does not have enough free storage space!");

            foreach (var file in toSync)
            {
                SyncFile(file, sourcePath, targetPath, dryRun, copyAcl, copyDates);
            }
        }

        static bool CompareAcl(FileInfo sourceFileInfo, FileInfo targetFileInfo)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return true;

            if (!sourceFileInfo.Exists || !targetFileInfo.Exists) return false;

            var sourceAcl = sourceFileInfo.GetAccessControl();
            var targetAcl = targetFileInfo.GetAccessControl();
            return sourceAcl.GetSecurityDescriptorSddlForm(AccessControlSections.All) ==
                   targetAcl.GetSecurityDescriptorSddlForm(AccessControlSections.All);
        }

        static void RemoveOrphanFiles(string sourcePath, string targetPath, bool dryRun)
        {
            Log("Calculating files to remove...");

            var toRemove = Directory.EnumerateFiles(targetPath, "*", SearchOption.AllDirectories)
                                    .Where(file => !File.Exists(file.Replace(targetPath, sourcePath)) && IsIncluded(file) && !IsIgnored(file))
                                    .ToList();

            _toRemove = toRemove.Count;

            foreach (var file in toRemove)
            {
                Log($"- Deleting [{(_removed + 1)}/{_toRemove}]: {file.Replace(targetPath, string.Empty).TrimStart('/').TrimStart('\\')}");

                try
                {
                    if (!dryRun)
                    {
                        File.Delete(file);
                    }

                    _removed++;
                }
                catch (Exception ex)
                {
                    Log(ex);
                    _errors++;
                }
            }
        }

        static void SyncFile(string file, string sourcePath, string targetPath, bool dryRun, bool copyAcl, bool copyDates)
        {
            var tmpTargetPath = Path.Combine(Path.GetDirectoryName(file.Replace(sourcePath, targetPath)) ?? string.Empty, $"{Path.GetFileName(file)}.tmp");
            var finalTargetPath = file.Replace(sourcePath, targetPath);

            Log($"- Syncing [{(_synced + 1)}/{_toSync}]: {file.Replace(sourcePath, string.Empty).TrimStart('/').TrimStart('\\')}");

            try
            {
                if (!dryRun)
                {
                    var targetDirectory = Path.GetDirectoryName(tmpTargetPath);
                    if (!string.IsNullOrEmpty(targetDirectory) && !Directory.Exists(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);

                    using (var fsSource = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var fsTarget = File.Open(tmpTargetPath, FileMode.Create, FileAccess.ReadWrite))
                    {
                        CopyWithProgress(fsSource, fsTarget, fsSource.Length);
                    }

                    File.Move(tmpTargetPath, finalTargetPath, true);

                    var fileSourceInfo = new FileInfo(file);
                    var fileTargetInfo = new FileInfo(finalTargetPath);

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && copyAcl)
                    {
                        var acl = fileSourceInfo.GetAccessControl();
                        fileTargetInfo.SetAccessControl(acl);
                    }

                    if (copyDates)
                    {
                        File.SetLastWriteTimeUtc(fileTargetInfo.FullName, fileSourceInfo.LastWriteTimeUtc);
                        File.SetCreationTimeUtc(fileTargetInfo.FullName, fileSourceInfo.CreationTimeUtc);
                    }
                }

                _synced++;
            }
            catch (Exception ex)
            {
                Log(ex);
                _errors++;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="source">Source stream</param>
        /// <param name="target">Target stream</param>
        /// <param name="totalBytes">Total source size</param>
        /// <param name="bufferSize">Buffer size (Default: 81920 = 80 KB)</param>
        static void CopyWithProgress(Stream source, Stream target, long totalBytes, int bufferSize = 81920)
        {
            byte[] buffer = new byte[bufferSize];
            long totalRead = 0;
            int read;
            int lastProgress = 0;

            // Stopwatch to measure time
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                target.Write(buffer, 0, read);
                totalRead += read;

                // Calculate progress
                int progress = (int)((totalRead / (double)totalBytes) * 100);

                // Calculate speed (bytes per second)
                double speed = totalRead / stopwatch.Elapsed.TotalSeconds;

                if (progress != lastProgress)
                {
                    Console.Write($"\rProgress: {progress}%, Speed: {FormatBytes((long)speed)}/s, Transfered: {FormatBytes(totalRead)}");
                    lastProgress = progress;
                }
            }

            // Ensure the final 100% progress is printed
            stopwatch.Stop();
            Log($"\rProgress: 100%, Speed: {FormatBytes((long)(totalRead / stopwatch.Elapsed.TotalSeconds))}/s, Transfered: {FormatBytes(totalRead)} Time: {stopwatch.Elapsed}",
                $"Speed: {FormatBytes((long)(totalRead / stopwatch.Elapsed.TotalSeconds))}/s, Transfered: {FormatBytes(totalRead)} Time: {stopwatch.Elapsed}");
        }
    }
}