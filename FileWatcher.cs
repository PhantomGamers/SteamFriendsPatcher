using SteamFriendsPatcher.Forms;
using SteamFriendsPatcher.Properties;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

using static SteamFriendsPatcher.Program;
using static SteamFriendsPatcher.Utilities;

namespace SteamFriendsPatcher
{
    class FileWatcher
    {
        // FileSystemWatchers
        public static FileSystemWatcher cacheWatcher;
        public static FileSystemWatcher crashWatcher;
        public static FileSystemWatcher libraryWatcher;

        public static FileStream cacheLock;
        public static bool scannerExists;
        public static bool friendslistWatcherExists;


        private static readonly object ScannerLock = new object();

        private static readonly MainWindow Main = App.MainWindowRef;


        private static readonly List<string> PendingCacheFiles = new List<string>();

        // location of Steam's CEF Cache
        private static readonly string SteamCacheDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Steam\\htmlcache\\Cache\\");

        public static void ToggleCacheScanner(bool isEnabled)
        {
            lock (ScannerLock)
            {
                Main.ToggleButtons(false);
                DirectoryInfo cacheDir;
                if (!Directory.Exists(SteamCacheDir))
                {
                    cacheDir = Directory.CreateDirectory(SteamCacheDir);
                    cacheDir.Refresh();
                }
                else
                {
                    cacheDir = new DirectoryInfo(SteamCacheDir);
                }

                if (scannerExists)
                {
                    if (cacheWatcher != null) cacheWatcher.EnableRaisingEvents = isEnabled;
                    if (crashWatcher != null) crashWatcher.EnableRaisingEvents = isEnabled;
                    if (libraryWatcher != null) libraryWatcher.EnableRaisingEvents = isEnabled;
                    scannerExists = isEnabled;
                    if (!isEnabled)
                    {
                        cacheLock.Dispose();
                        if (File.Exists(Path.Combine(SteamCacheDir, "tmp.lock")))
                            File.Delete(Path.Combine(SteamCacheDir, "tmp.lock"));
                    }

                    Automation.RemoveAllEventHandlers();
                    friendslistWatcherExists = false;
                    Program.Print("Cache Watcher " + (isEnabled ? "Started" : "Stopped") + ".");
                    Main.ToggleButtons(true);
                    return;
                }

                if (!isEnabled) return;

                for (var i = 0; i < 10; i++)
                {
                    try
                    {
                        cacheLock = new FileStream(Path.Combine(SteamCacheDir, "tmp.lock"), FileMode.Create,
                            FileAccess.ReadWrite, FileShare.None);
                    }
                    catch
                    {
                        Program.Print("Windows is dumb.", Program.LogLevel.Debug);
                        Program.Print("Does cache directory exist: " + cacheDir.Exists, Program.LogLevel.Debug);
                        Task.Delay(TimeSpan.FromSeconds(1)).Wait();
                        continue;
                    }

                    break;
                }

                if (!File.Exists(Path.Combine(SteamCacheDir, "tmp.lock")))
                {
                    Program.Print("Could not lock Cache. Scanner can not be started.", Program.LogLevel.Error);
                    return;
                }

                StartFriendsListWatcher();

                StartCrashScanner();

                if (Settings.Default.patchLibraryBeta && Directory.Exists(Path.Combine(Program.LibraryUIDir, "css")))
                {
                    StartLibraryScanner();
                }

                cacheWatcher = new FileSystemWatcher
                {
                    Path = SteamCacheDir,
                    NotifyFilter = NotifyFilters.LastAccess
                                   | NotifyFilters.LastWrite
                                   | NotifyFilters.FileName
                                   | NotifyFilters.Size,
                    Filter = "f_*"
                };
                cacheWatcher.Created += CacheWatcher_Changed;
                cacheWatcher.Changed += CacheWatcher_Changed;
                Program.GetLatestFriendsCss();
                cacheWatcher.EnableRaisingEvents = true;
                scannerExists = true;
                Program.Print("Cache Watcher Started.");

                Main.ToggleButtons(true);
            }
        }

        private static void StartCrashScanner()
        {
            if (!Directory.Exists(Program.steamDir))
            {
                Program.Print("Steam directory not found.", Program.LogLevel.Warning);
                return;
            }

            crashWatcher = new FileSystemWatcher
            {
                Path = Program.steamDir,
                NotifyFilter = NotifyFilters.LastAccess
                               | NotifyFilters.LastWrite
                               | NotifyFilters.FileName,
                Filter = ".crash"
            };
            crashWatcher.Created += CrashWatcher_Event;
            crashWatcher.Changed += CrashWatcher_Event;
            crashWatcher.Deleted += CrashWatcher_Event;

            crashWatcher.EnableRaisingEvents = true;

            Program.Print("Crash scanner started.", Program.LogLevel.Debug);
        }

        private static void CrashWatcher_Event(object sender, FileSystemEventArgs e)
        {
            switch (e.ChangeType)
            {
                case WatcherChangeTypes.Changed:
                case WatcherChangeTypes.Created:
                    {
                        Program.Print("Steam start detected.", Program.LogLevel.Debug);
                        Program.GetLatestFriendsCss();
                        if (scannerExists) StartFriendsListWatcher();

                        break;
                    }

                case WatcherChangeTypes.Deleted:
                    Program.Print("Steam graceful shutdown detected.", Program.LogLevel.Debug);
                    friendslistWatcherExists = false;
                    Automation.RemoveAllEventHandlers();
                    break;
            }
        }

        private static void StartLibraryScanner()
        {
            if (!Directory.Exists(Program.steamDir))
            {
                Program.Print("Steam directory not found.", Program.LogLevel.Warning);
                return;
            }

            libraryWatcher = new FileSystemWatcher
            {
                Path = Path.Combine(Program.LibraryUIDir, "css"),
                NotifyFilter = NotifyFilters.LastAccess
                               | NotifyFilters.LastWrite
                               | NotifyFilters.FileName,
                Filter = "libraryroot.css"
            };
            libraryWatcher.Created += LibraryWatcher_Event;
            libraryWatcher.Changed += LibraryWatcher_Event;

            libraryWatcher.EnableRaisingEvents = true;

            Program.Print("Library scanner started.", Program.LogLevel.Debug);
        }

        private static void LibraryWatcher_Event(object sender, FileSystemEventArgs e)
        {
            switch (e.ChangeType)
            {
                case WatcherChangeTypes.Changed:
                case WatcherChangeTypes.Created:
                    {
                        Program.Print("Library change detected.", Program.LogLevel.Debug);
                        Program.PatchLibrary();
                        break;
                    }
            }
        }

        private static void StartFriendsListWatcher()
        {
            if (friendslistWatcherExists || Process.GetProcessesByName("Steam").FirstOrDefault() == null) return;
            friendslistWatcherExists = true;
            Automation.AddAutomationEventHandler(WindowPattern.WindowOpenedEvent, AutomationElement.RootElement,
                TreeScope.Children, (sender, e) =>
                {
                    if (!(sender is AutomationElement element)) return;
                    try
                    {
                        if (element.Current.ClassName == "SDL_app") Program.GetLatestFriendsCss();
                    }
                    catch (ElementNotAvailableException)
                    {

                    }
                });
        }

        private static void CacheWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (PendingCacheFiles.Contains(e.Name) || !updatePending &&
                (friendscss == null || new FileInfo(e.FullPath).Length != friendscss.Length))
                return;
            PendingCacheFiles.Add(e.Name);
            var t = new Thread(ProcessCacheFileEvent);
            t.Start(e);
        }

        private static void ProcessCacheFileEvent(object obj)
        {
            while (updatePending) Task.Delay(TimeSpan.FromMilliseconds(20)).Wait();

            if (!(obj is FileSystemEventArgs e)) return;
            Print($"New file found: {e.Name}", LogLevel.Debug);
            DateTime lastAccess, lastWrite;
            long size;
            var timer = new Stopwatch();
            timer.Start();
            do
            {
                lastAccess = File.GetLastAccessTime(e.FullPath);
                lastWrite = File.GetLastWriteTime(e.FullPath);
                size = new FileInfo(e.FullPath).Length;
                Task.Delay(TimeSpan.FromMilliseconds(500)).Wait();
            } while ((lastAccess != File.GetLastAccessTime(e.FullPath)
                      || lastWrite != File.GetLastWriteTime(e.FullPath)
                      || size != new FileInfo(e.FullPath).Length) && timer.Elapsed < TimeSpan.FromSeconds(15));

            timer.Stop();
            if (timer.Elapsed > TimeSpan.FromSeconds(15))
            {
                Print($"{e.Name} kept changing, blacklisting...", LogLevel.Debug);
                return;
            }

            timer.Restart();
            while (!IsFileReady(e.FullPath) && timer.Elapsed < TimeSpan.FromSeconds(15))
                Task.Delay(TimeSpan.FromMilliseconds(20)).Wait();
            timer.Stop();
            if (timer.Elapsed > TimeSpan.FromSeconds(15))
            {
                Print($"{e.Name} could not be read, blacklisting...", LogLevel.Debug);
                return;
            }

            byte[] cachefile;
            try
            {
                using (var f = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    cachefile = new byte[f.Length];
                    f.Read(cachefile, 0, cachefile.Length);
                }
            }
            catch
            {
                Task.Delay(TimeSpan.FromSeconds(2)).Wait();
                if (!File.Exists(e.FullPath)) return;
                Print($"Error opening file {e.Name}, retrying.", LogLevel.Debug);
                PendingCacheFiles.Remove(e.Name);
                if (PendingCacheFiles.Contains(e.Name))
                {
                    Print($"Multiple occurrences of {e.Name} found in list, removing all...", LogLevel.Debug);
                    do
                    {
                        PendingCacheFiles.Remove(e.Name);
                    } while (PendingCacheFiles.Contains(e.Name));
                }

                ProcessCacheFileEvent(e);
                return;
            }

            if (!IsGZipHeader(cachefile))
            {
                Print($"{e.Name} not a gzip file.", LogLevel.Debug);
                PendingCacheFiles.Remove(e.Name);
                if (!PendingCacheFiles.Contains(e.Name)) return;
                Print($"Multiple occurrences of {e.Name} found in list, removing all...", LogLevel.Debug);
                do
                {
                    PendingCacheFiles.Remove(e.Name);
                } while (PendingCacheFiles.Contains(e.Name));

                return;
            }

            if (friendscss.Length == cachefile.Length && ByteArrayCompare(friendscss, cachefile))
                PatchCacheFile(e.FullPath, Decompress(cachefile));
            else
                Print($"{e.Name} did not match.", LogLevel.Debug);
            PendingCacheFiles.Remove(e.Name);
            if (!PendingCacheFiles.Contains(e.Name)) return;
            Print($"Multiple occurrences of {e.Name} found in list, removing all...", LogLevel.Debug);
            do
            {
                PendingCacheFiles.Remove(e.Name);
            } while (PendingCacheFiles.Contains(e.Name));

            /*
                decompressedcachefile = Decompress(cachefile);

                if (decompressedcachefile.Length == friendscss.Length &&
                    ByteArrayCompare(decompressedcachefile, friendscss))
                {
                    PatchCacheFile(e.FullPath, decompressedcachefile);
                }
                else
                {
                    Print($"{e.Name} did not match.", LogLevel.Debug);
                }
                pendingCacheFiles.Remove(e.Name);
                if(pendingCacheFiles.Contains(e.Name))
                {
                    Print($"Multiple occurrences of {e.Name} found in list, removing all...", LogLevel.Debug);
                    do
                    {
                        pendingCacheFiles.Remove(e.Name);
                    } while (pendingCacheFiles.Contains(e.Name));
                }
                return;
                */
        }

        private static bool IsFileReady(string filename)
        {
            // If the file can be opened for exclusive access it means that the file
            // is no longer locked by another process.
            try
            {
                using (var inputStream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return inputStream.Length > 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
