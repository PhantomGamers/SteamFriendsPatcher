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
    internal class FileWatcher
    {
        // FileSystemWatchers
        public static FileSystemWatcher cacheWatcher;
        public static FileSystemWatcher crashWatcher;
        public static FileSystemWatcher libraryWatcher;

        public static FileStream cacheLock;
        public static bool scannerExists;
        public static bool friendslistWatcherExists;

        public static string libraryRootCss
        {
            get
            {
                string cssdir = Path.Combine(LibraryUIDir, "css");
                if (!File.Exists(Path.Combine(cssdir, Settings.Default.libraryRootCss)))
                {
                    string css_5 = "5.css";
                    string css_6 = "6.css";
                    Settings.Default.libraryRootCss = File.Exists(Path.Combine(cssdir, css_6)) ? css_6 : css_5;
                }
                return Settings.Default.libraryRootCss;
            }
            set => Settings.Default.libraryRootCss = value;
        }


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
                    if (cacheWatcher != null)
                    {
                        cacheWatcher.EnableRaisingEvents = isEnabled;
                    }

                    if (crashWatcher != null)
                    {
                        crashWatcher.EnableRaisingEvents = isEnabled;
                    }

                    if (libraryWatcher != null)
                    {
                        libraryWatcher.EnableRaisingEvents = isEnabled;
                    }

                    scannerExists = isEnabled;
                    if (!isEnabled)
                    {
                        cacheLock.Dispose();
                        if (File.Exists(Path.Combine(SteamCacheDir, "tmp.lock")))
                        {
                            File.Delete(Path.Combine(SteamCacheDir, "tmp.lock"));
                        }
                    }

                    Automation.RemoveAllEventHandlers();
                    friendslistWatcherExists = false;
                    Print("Cache Watcher " + (isEnabled ? "Started" : "Stopped") + ".");
                    Main.ToggleButtons(true);
                    return;
                }

                if (!isEnabled)
                {
                    return;
                }

                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        cacheLock = new FileStream(Path.Combine(SteamCacheDir, "tmp.lock"), FileMode.Create,
                            FileAccess.ReadWrite, FileShare.None);
                    }
                    catch
                    {
                        Print("Windows is dumb.", LogLevel.Debug);
                        Print("Does cache directory exist: " + cacheDir.Exists, LogLevel.Debug);
                        Task.Delay(TimeSpan.FromSeconds(1)).Wait();
                        continue;
                    }

                    break;
                }

                if (!File.Exists(Path.Combine(SteamCacheDir, "tmp.lock")))
                {
                    Print("Could not lock Cache. Scanner can not be started.", LogLevel.Error);
                    return;
                }

                StartFriendsListWatcher();

                StartCrashScanner();

                if (Settings.Default.patchLibraryBeta && Directory.Exists(Path.Combine(LibraryUIDir, "css")))
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
                _ = GetLatestFriendsCss();
                cacheWatcher.EnableRaisingEvents = true;
                scannerExists = true;
                Print("Cache Watcher Started.");

                Main.ToggleButtons(true);
            }
        }

        private static void StartCrashScanner()
        {
            if (!Directory.Exists(steamDir))
            {
                Print("Steam directory not found.", LogLevel.Warning);
                return;
            }

            crashWatcher = new FileSystemWatcher
            {
                Path = steamDir,
                NotifyFilter = NotifyFilters.LastAccess
                               | NotifyFilters.LastWrite
                               | NotifyFilters.FileName,
                Filter = ".crash"
            };
            crashWatcher.Created += CrashWatcher_Event;
            crashWatcher.Changed += CrashWatcher_Event;
            crashWatcher.Deleted += CrashWatcher_Event;

            crashWatcher.EnableRaisingEvents = true;

            Print("Crash scanner started.", LogLevel.Debug);
        }

        private static void CrashWatcher_Event(object sender, FileSystemEventArgs e)
        {
            switch (e.ChangeType)
            {
                case WatcherChangeTypes.Changed:
                case WatcherChangeTypes.Created:
                    {
                        Print("Steam start detected.", LogLevel.Debug);
                        GetLatestFriendsCss();
                        if (scannerExists)
                        {
                            StartFriendsListWatcher();
                        }

                        break;
                    }

                case WatcherChangeTypes.Deleted:
                    Print("Steam graceful shutdown detected.", LogLevel.Debug);
                    friendslistWatcherExists = false;
                    Automation.RemoveAllEventHandlers();
                    break;
                case WatcherChangeTypes.Renamed:
                    break;
                case WatcherChangeTypes.All:
                    break;
                default:
                    break;
            }
        }

        private static void StartLibraryScanner()
        {
            if (!Directory.Exists(steamDir))
            {
                Print("Steam directory not found.", LogLevel.Warning);
                return;
            }

            libraryWatcher = new FileSystemWatcher
            {
                Path = Path.Combine(LibraryUIDir, "css"),
                NotifyFilter = NotifyFilters.LastAccess
                               | NotifyFilters.LastWrite
                               | NotifyFilters.FileName,
                Filter = libraryRootCss
            };
            libraryWatcher.Created += LibraryWatcher_Event;
            libraryWatcher.Changed += LibraryWatcher_Event;

            libraryWatcher.EnableRaisingEvents = true;

            Print("Library scanner started.", LogLevel.Debug);
        }

        private static void LibraryWatcher_Event(object sender, FileSystemEventArgs e)
        {
            switch (e.ChangeType)
            {
                case WatcherChangeTypes.Changed:
                case WatcherChangeTypes.Created:
                    {
                        Print("Library change detected.", LogLevel.Debug);
                        PatchLibrary();
                        break;
                    }
            }
        }

        private static void StartFriendsListWatcher()
        {
            if (friendslistWatcherExists || Process.GetProcessesByName("Steam").FirstOrDefault() == null)
            {
                return;
            }

            friendslistWatcherExists = true;
            Automation.AddAutomationEventHandler(WindowPattern.WindowOpenedEvent, AutomationElement.RootElement,
                TreeScope.Children, (sender, e) =>
                {
                    if (!(sender is AutomationElement element))
                    {
                        return;
                    }

                    try
                    {
                        if (element.Current.ClassName == "SDL_app")
                        {
                            GetLatestFriendsCss();
                        }
                    }
                    catch (ElementNotAvailableException)
                    {

                    }
                });
        }

        private static void CacheWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (PendingCacheFiles.Contains(e.Name) || !updatePending &&
                friendsCssCrcs[0] == null && friendsCssCrcs[1] == null)
            {
                return;
            }

            PendingCacheFiles.Add(e.Name);
            var t = new Thread(ProcessCacheFileEvent);
            t.Start(e);
        }

        private static void ProcessCacheFileEvent(object obj)
        {
            while (updatePending)
            {
                Task.Delay(TimeSpan.FromMilliseconds(20)).Wait();
            }

            if (!(obj is FileSystemEventArgs e))
            {
                return;
            }

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
            {
                Task.Delay(TimeSpan.FromMilliseconds(20)).Wait();
            }

            timer.Stop();
            if (timer.Elapsed > TimeSpan.FromSeconds(15))
            {
                Print($"{e.Name} could not be read, blacklisting...", LogLevel.Debug);
                return;
            }

            for (int i = 0; i < friendsCssUrls.Count; i++)
            {
                try
                {
                    if (CompareCRC(e.FullPath, friendsCssCrcs.ElementAt(i)))
                    {
                        byte[] cachefile;
                        using (var f = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            cachefile = new byte[f.Length];
                            _ = f.Read(cachefile, 0, cachefile.Length);
                        }
                        PatchCacheFile(e.FullPath, Decompress(cachefile), i);
                    }
                    else
                    {
                        Print($"{e.Name} did not match.", LogLevel.Debug);
                    }
                }
                catch
                {
                    Task.Delay(TimeSpan.FromSeconds(2)).Wait();
                    if (!File.Exists(e.FullPath))
                    {
                        continue;
                    }

                    Print($"Error opening file {e.Name}, retrying.", LogLevel.Debug);
                    _ = PendingCacheFiles.Remove(e.Name);
                    if (PendingCacheFiles.Contains(e.Name))
                    {
                        Print($"Multiple occurrences of {e.Name} found in list, removing all...", LogLevel.Debug);
                        do
                        {
                            PendingCacheFiles.Remove(e.Name);
                        } while (PendingCacheFiles.Contains(e.Name));
                    }

                    ProcessCacheFileEvent(e);
                    continue;
                }
            }
            _ = PendingCacheFiles.Remove(e.Name);
            if (!PendingCacheFiles.Contains(e.Name))
            {
                return;
            }

            Print($"Multiple occurrences of {e.Name} found in list, removing all...", LogLevel.Debug);
            do
            {
                _ = PendingCacheFiles.Remove(e.Name);
            } while (PendingCacheFiles.Contains(e.Name));
        }

        private static bool IsFileReady(string filename)
        {
            // If the file can be opened for exclusive access it means that the file
            // is no longer locked by another process.
            try
            {
                using (FileStream inputStream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.None))
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
