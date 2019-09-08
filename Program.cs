using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Media;
using IWshRuntimeLibrary;
using Microsoft.Win32;
using Semver;
using SteamFriendsPatcher.Forms;
using SteamFriendsPatcher.Properties;
using File = System.IO.File;

namespace SteamFriendsPatcher
{
    internal class Program
    {
        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        // location of steam directory
        public static string steamDir = FindSteamDir();

        // location of Steam's CEF Cache
        private static readonly string SteamCacheDir =
            Path.Combine(Environment.GetEnvironmentVariable("LocalAppData") ?? throw new InvalidOperationException(),
                "Steam\\htmlcache\\Cache\\");

        // Link to startup file
        public static readonly string StartupLink = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs\Startup",
            Assembly.GetExecutingAssembly().GetName().Name + ".lnk");

        public static readonly string StartupLinkOld = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs\Startup",
            Assembly.GetExecutingAssembly().GetName().Name + ".url");

        // friends.css etag
        private static string _etag;

        // original friends.css
        public static byte[] friendscss;

        // patched friends.css
        public static byte[] friendscsspatched;

        public static string friendscssetag;

        // original friends.css age
        public static DateTime friendscssage;

        // FileSystemWatchers
        public static FileSystemWatcher cacheWatcher;

        public static FileSystemWatcher crashWatcher;
        public static FileStream cacheLock;
        public static bool scannerExists;
        public static bool friendslistWatcherExists;
        public static bool updatePending;

        private static readonly List<string> PendingCacheFiles = new List<string>();

        // objects to lock to maintain thread safety
        private static readonly object MessageLock = new object();

        private static readonly object ScannerLock = new object();
        private static readonly object UpdateScannerLock = new object();

        private static readonly object GetFriendsCssLock = new object();
        // private static readonly object LogPrintLock = new object();

        private static readonly MainWindow Main = App.MainWindowRef;

        public static bool UpdateChecker()
        {
            lock (UpdateScannerLock)
            {
                Print("Checking for updates...");
                try
                {
                    var wc = new WebClient();
                    ServicePointManager.SecurityProtocol =
                        SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                    wc.Headers.Add("user-agent", Assembly.GetExecutingAssembly().FullName);
                    var latestver =
                        wc.DownloadString(
                            "https://api.github.com/repos/phantomgamers/steamfriendspatcher/releases/latest");
                    const string verregex = "(?<=\"tag_name\":\")(.*?)(?=\")";
                    var latestvervalue = Regex.Match(latestver, verregex).Value;
                    if (!string.IsNullOrEmpty(latestvervalue))
                    {
                        var assemblyVer = ThisAssembly.AssemblyInformationalVersion;
                        assemblyVer = assemblyVer.Substring(0,
                            assemblyVer.IndexOf('+') > -1 ? assemblyVer.IndexOf('+') : assemblyVer.Length);
                        if (!SemVersion.TryParse(assemblyVer, out var localVer) ||
                            !SemVersion.TryParse(latestvervalue, out var remoteVer))
                        {
                            Print("Update check failed, failed to parse version string.", LogLevel.Error);
                            return false;
                        }

                        if (remoteVer > localVer)
                        {
                            if (MessageBox.Show("Update available. Download now?",
                                    "Steam Friends Patcher - Update Available", MessageBoxButton.YesNo) ==
                                MessageBoxResult.Yes)
                                Process.Start("https://github.com/PhantomGamers/SteamFriendsPatcher/releases/latest");

                            return true;
                        }

                        Print("No updates found.");
                        return false;
                    }

                    Print("Failed to check for updates.", LogLevel.Error);
                    return false;
                }
                catch (WebException we)
                {
                    Print("Failed to check for updates.", LogLevel.Error);
                    Print(we.ToString(), LogLevel.Error);
                }

                return false;
            }
        }

        private static void PatchCacheFile(string friendscachefile, IEnumerable<byte> decompressedcachefile)
        {
            Print($"Successfully found matching friends.css at {friendscachefile}.");
            File.WriteAllBytes(steamDir + "\\clientui\\friends.original.css",
                Encoding.ASCII.GetBytes("/*" + _etag + "*/\n").Concat(decompressedcachefile).ToArray());

            Print("Overwriting with patched version...");
            File.WriteAllBytes(friendscachefile, friendscsspatched);

            if (!File.Exists(steamDir + "\\clientui\\friends.custom.css"))
                File.Create(steamDir + "\\clientui\\friends.custom.css").Dispose();

            if (Process.GetProcessesByName("Steam").FirstOrDefault() != null &&
                File.Exists(steamDir + "\\clientui\\friends.custom.css") && FindFriendsWindow())
            {
                Print("Reloading friends window...");
                Process.Start(steamDir + "\\Steam.exe", @"steam://friends/status/offline");
                Task.Delay(TimeSpan.FromSeconds(1)).Wait();
                Process.Start(steamDir + "\\Steam.exe", @"steam://friends/status/online");
            }

            Print("Done! Put your custom css in " + steamDir + "\\clientui\\friends.custom.css");
            Print("Close and reopen your Steam friends window to see changes.");

            Main.Dispatcher.Invoke(() =>
            {
                if (Main.IsVisible || !Settings.Default.showNotificationsInTray) return;
                Main.NotifyIcon.BalloonTipTitle = @"Steam Friends Patcher";
                Main.NotifyIcon.BalloonTipText = @"Successfully patched friends!";
                Main.NotifyIcon.ShowBalloonTip((int) TimeSpan.FromSeconds(10).TotalMilliseconds);
            });
        }

        public static void FindCacheFile(bool forceUpdate = false)
        {
            var preScannerStatus = scannerExists;
            ToggleCacheScanner(false);
            Main.ToggleButtons(false);

            Print("Force scan started.");
            GetLatestFriendsCss(forceUpdate);

            while (updatePending) Task.Delay(TimeSpan.FromMilliseconds(20)).Wait();

            if (friendscss == null || friendscsspatched == null)
            {
                Print("Friends.css could not be obtained, ending force check...");
                goto ResetButtons;
            }

            Print("Finding list of possible cache files...");
            if (!Directory.Exists(SteamCacheDir))
            {
                Print("Cache folder does not exist.", LogLevel.Error);
                Print("Please confirm that Steam is running and that the friends list is open and try again.",
                    LogLevel.Error);
                goto ResetButtons;
            }

            var validFiles = new DirectoryInfo(SteamCacheDir).EnumerateFiles("f_*", SearchOption.TopDirectoryOnly)
                .Where(f => f.Length == friendscsspatched.Length || f.Length == friendscss.Length)
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => f.FullName)
                .ToList();
            var count = validFiles.Count;
            if (count == 0)
            {
                Print("No cache files found.", LogLevel.Error);
                Print("Please confirm that Steam is running and that the friends list is open and try again.",
                    LogLevel.Error);
                goto ResetButtons;
            }

            Print($"Found {count} possible cache files.");

            string friendscachefile = null;
            var patchedFileFound = false;

            Print("Checking cache files for match...");
            Parallel.ForEach(validFiles, (s, state) =>
            {
                byte[] cachefile;

                try
                {
                    using (var f = new FileStream(s, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        cachefile = new byte[f.Length];
                        f.Read(cachefile, 0, cachefile.Length);
                    }
                }
                catch
                {
                    Print($"Error, {s} could not be opened.", LogLevel.Debug);
                    return;
                }

                if (!IsGZipHeader(cachefile)) return;
                if (cachefile.Length == friendscss.Length && ByteArrayCompare(cachefile, friendscss))
                {
                    state.Stop();
                    friendscachefile = s;
                    PatchCacheFile(s, Decompress(cachefile));
                }
                else if (cachefile.Length == friendscsspatched.Length && ByteArrayCompare(cachefile, friendscsspatched))
                {
                    patchedFileFound = true;
                }

                /*
                    if (useDecompressionMethod)
                    {
                        byte[] decompressedcachefile = Decompress(cachefile);
                        if (decompressedcachefile.Length == friendscss.Length && ByteArrayCompare(decompressedcachefile, friendscss))
                        {
                            state.Stop();
                            friendscachefile = s;
                            PatchCacheFile(s, decompressedcachefile);
                            return;
                        }
                        else if (decompressedcachefile.Length == friendscsspatched.Length && ByteArrayCompare(decompressedcachefile, friendscsspatched))
                        {
                            patchedFileFound = true;
                        }
                    }
                    */
            });

            if (string.IsNullOrEmpty(friendscachefile))
            {
                if (!patchedFileFound)
                    Print("Cache file does not exist or is outdated.", LogLevel.Warning);
                else
                    Print("Cache file is already patched.");
            }

            ResetButtons:
            Main.ToggleButtons(true);
            if (preScannerStatus) ToggleCacheScanner(true);
        }

        private static byte[] PrependFile(IEnumerable<byte> file)
        {
            // custom only
            // string appendText = "@import url(\"https://steamloopback.host/friends.custom.css\");\n";

            // custom overrides original (!important tags not needed)
            const string appendText =
                "@import url(\"https://steamloopback.host/friends.original.css\");\n@import url(\"https://steamloopback.host/friends.custom.css\");\n";

            // original overrides custom (!important tags needed, this is the original behavior)
            // string appendText = "@import url(\"https://steamloopback.host/friends.custom.css\");\n@import url(\"https://steamloopback.host/friends.original.css\");\n{";

            // load original from Steam CDN, not recommended because of infinite matching
            // string appendText = "@import url(\"https://steamcommunity-a.akamaihd.net/public/css/webui/friends.css\");\n@import url(\"https://steamloopback.host/friends.custom.css\");\n";
            var append = Encoding.ASCII.GetBytes(appendText);

            var output = append.Concat(file).Concat(Encoding.ASCII.GetBytes("}")).ToArray();
            return output;
        }

        public static bool GetLatestFriendsCss(bool force = false)
        {
            if (DateTime.Now.Subtract(friendscssage).TotalMinutes < 1 && !force || updatePending) return true;
            lock (GetFriendsCssLock)
            {
                updatePending = true;
                Print("Checking for latest friends.css...");
                using (var wc = new WebClient())
                {
                    try
                    {
                        ServicePointManager.SecurityProtocol =
                            SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

                        var steamChat = wc.DownloadString("https://steam-chat.com/chat/clientui/?l=&build=&cc");
                        wc.Headers[HttpRequestHeader.AcceptEncoding] = "gzip";
                        wc.Encoding = Encoding.UTF8;
                        const string eTagRegex =
                            "(?<=<link href=\"https:\\/\\/steamcommunity-a.akamaihd.net\\/public\\/css\\/webui\\/friends.css\\?v=)(.*?)(?=\")";
                        _etag = Regex.Match(steamChat, eTagRegex).Value;

                        if (!string.IsNullOrEmpty(_etag) && !string.IsNullOrEmpty(friendscssetag) &&
                            _etag == friendscssetag)
                        {
                            Print("friends.css is already up to date.");
                            updatePending = false;
                            return true;
                        }

                        if (string.IsNullOrEmpty(_etag))
                        {
                            wc.DownloadData("https://steamcommunity-a.akamaihd.net/public/css/webui/friends.css");
                            var count = wc.ResponseHeaders.Count;
                            for (var i = 0; i < count; i++)
                            {
                                if (wc.ResponseHeaders.GetKey(i) != "ETag") continue;
                                _etag = wc.ResponseHeaders.Get(i);
                                break;
                            }
                        }

                        if (!string.IsNullOrEmpty(_etag))
                        {
                            var fc = wc.DownloadData(
                                "https://steamcommunity-a.akamaihd.net/public/css/webui/friends.css?v=" + _etag);
                            if (fc.Length > 0)
                            {
                                friendscss = fc;
                                var tmp = PrependFile(Decompress(fc));

                                using (var file = new MemoryStream())
                                {
                                    using (var gzip = new GZipStream(file, CompressionLevel.Optimal, false))
                                    {
                                        gzip.Write(tmp, 0, tmp.Length);
                                    }

                                    friendscsspatched = file.ToArray();
                                }

                                friendscssage = DateTime.Now;
                                friendscssetag = _etag;
                                Print("Successfully downloaded latest friends.css");
                                updatePending = false;
                                return true;
                            }
                        }

                        /*
                        else
                        {
                            fc = wc.DownloadData("https://steamcommunity-a.akamaihd.net/public/css/webui/friends.css");
                            if (fc.Length > 0)
                            {
                                friendscss = Decompress(fc);
                                friendscsspatched = PrependFile(friendscss);
                                friendscssage = DateTime.Now;
                                Print("Successfully downloaded latest friends.css.");
                                return true;
                            }
                        }
                        */
                        Print("Failed to download friends.css", LogLevel.Error);
                        updatePending = false;
                        return false;
                    }
                    catch (WebException we)
                    {
                        Print("Failed to download friends.css.", LogLevel.Error);
                        Print(we.ToString(), LogLevel.Error);
                        updatePending = false;
                        return false;
                    }
                }
            }
        }

        private static string FindSteamDir()
        {
            using (var registryKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam"))
            {
                string filePath = null;
                var regFilePath = registryKey?.GetValue("SteamPath");
                if (regFilePath != null) filePath = regFilePath.ToString().Replace(@"/", @"\");

                return filePath;
            }
        }

        private static bool FindFriendsWindow()
        {
            return (int) NativeMethods.FindWindowByClass("SDL_app") != 0;
        }

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
                    cacheWatcher.EnableRaisingEvents = isEnabled;
                    crashWatcher.EnableRaisingEvents = isEnabled;
                    scannerExists = isEnabled;
                    if (!isEnabled)
                    {
                        cacheLock.Dispose();
                        if (File.Exists(Path.Combine(SteamCacheDir, "tmp.lock")))
                            File.Delete(Path.Combine(SteamCacheDir, "tmp.lock"));
                    }

                    Automation.RemoveAllEventHandlers();
                    friendslistWatcherExists = false;
                    Print("Cache Watcher " + (isEnabled ? "Started" : "Stopped") + ".");
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
                GetLatestFriendsCss();
                cacheWatcher.EnableRaisingEvents = true;
                scannerExists = true;
                Print("Cache Watcher Started.");

                Main.ToggleButtons(true);
            }
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
                    if (scannerExists) StartFriendsListWatcher();

                    break;
                }

                case WatcherChangeTypes.Deleted:
                    Print("Steam graceful shutdown detected.", LogLevel.Debug);
                    friendslistWatcherExists = false;
                    Automation.RemoveAllEventHandlers();
                    break;
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
                    if (element.Current.ClassName == "SDL_app") GetLatestFriendsCss();
                });
        }

        public static void ClearSteamCache()
        {
            if (!Directory.Exists(SteamCacheDir))
            {
                Print("Cache folder does not exist.", LogLevel.Warning);
                return;
            }

            var preScannerStatus = scannerExists;
            var preSteamStatus = Process.GetProcessesByName("Steam").FirstOrDefault() != null;
            if (preSteamStatus)
            {
                if (MessageBox.Show("Steam will need to be shutdown to clear cache. Restart automatically?",
                        "Steam Friends Patcher", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                var steamp = Process.GetProcessesByName("Steam").FirstOrDefault();
                Print("Shutting down Steam...");
                Process.Start(steamDir + "\\Steam.exe", "-shutdown");
                if (steamp != null && !steamp.WaitForExit((int) TimeSpan.FromSeconds(30).TotalMilliseconds))
                {
                    Print("Could not successfully shutdown Steam, please manually shutdown Steam and try again.",
                        LogLevel.Error);
                    Main.ToggleButtons(true);
                    return;
                }
            }

            ToggleCacheScanner(false);
            Main.ToggleButtons(false);

            Print("Deleting cache files...");
            try
            {
                Directory.Delete(SteamCacheDir, true);
            }
            catch (IOException ioe)
            {
                Print("Some cache files in use, cannot delete.", LogLevel.Error);
                Print(ioe.ToString(), LogLevel.Error);
            }
            finally
            {
                Print("Cache files deleted.");
            }

            ToggleCacheScanner(preScannerStatus);
            if (preSteamStatus)
            {
                Print("Restarting Steam...");
                Process.Start(steamDir + "\\Steam.exe", Settings.Default.steamLaunchArgs);
                for (var i = 0; i < 10; i++)
                {
                    if (Process.GetProcessesByName("Steam").FirstOrDefault() != null)
                    {
                        Print("Steam started.");
                        break;
                    }

                    if (i == 9) Print("Failed to start Steam.", LogLevel.Error);
                }
            }

            Main.ToggleButtons(true);
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

        private static IEnumerable<byte> Decompress(byte[] gzip)
        {
            // Create a GZIP stream with decompression mode.
            // ... Then create a buffer and write into while reading from the GZIP stream.
            using (var stream = new GZipStream(
                new MemoryStream(gzip),
                CompressionMode.Decompress))
            {
                const int size = 4096;
                var buffer = new byte[size];
                using (var memory = new MemoryStream())
                {
                    int count;
                    do
                    {
                        count = stream.Read(buffer, 0, size);
                        if (count > 0) memory.Write(buffer, 0, count);
                    } while (count > 0);

                    return memory.ToArray();
                }
            }
        }

        private static bool IsGZipHeader(IReadOnlyList<byte> arr)
        {
            return arr.Count >= 2 &&
                   arr[0] == 31 &&
                   arr[1] == 139;
        }

        private static bool ByteArrayCompare(byte[] b1, byte[] b2)
        {
            // Validate buffers are the same length.
            // This also ensures that the count does not exceed the length of either buffer.
            return b1.Length == b2.Length && NativeMethods.Memcmp(b1, b2, b1.Length) == 0;
        }

        public static void CreateStartUpShortcut()
        {
            var wsh = new WshShell();
            if (!(wsh.CreateShortcut(StartupLink) is IWshShortcut shortcut))
            {
                Print("Could not create startup shortcut.", LogLevel.Error);
                return;
            }

            shortcut.TargetPath = Assembly.GetExecutingAssembly().Location;
            shortcut.WorkingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            shortcut.IconLocation = Assembly.GetExecutingAssembly().Location;
            shortcut.Save();
        }

        public static void Print(string message = null, LogLevel logLevel = LogLevel.Info, bool newline = true)
        {
            var dateTime = DateTime.Now.ToString("G", CultureInfo.CurrentCulture);
#if DEBUG
            Debug.Write($"[{dateTime}][{logLevel}] {message}" + (newline ? Environment.NewLine : string.Empty));
#endif
            /*
            if (SteamFriendsPatcher.Properties.Settings.Default.outputToLog)
            {
                lock (LogPrintLock)
                {
                    FileInfo file = new FileInfo(Path.Combine(Environment.GetEnvironmentVariable("LocalAppData"), $"PhantomGamers/logs/SteamFriendsPatcher_"
                                       + $"{DateTime.Now.ToString("yyyyMMdd")}.log"));
                    Directory.CreateDirectory(file.Directory.FullName);
                    File.AppendAllText(file.FullName, fullMessage);
                }
            }
            */

            if (logLevel == LogLevel.Debug && !Settings.Default.showDebugMessages) return;

            lock (MessageLock)
            {
                Main.Output.Dispatcher.Invoke(() =>
                {
                    if (Main.Output.Document == null) Main.Output.Document = new FlowDocument();

                    // Date & Time
                    var tr = new TextRange(Main.Output.Document.ContentEnd, Main.Output.Document.ContentEnd)
                    {
                        Text = $"[{dateTime}] "
                    };
                    tr.ApplyPropertyValue(TextElement.ForegroundProperty,
                        (SolidColorBrush) new BrushConverter().ConvertFromString("#76608a") ??
                        throw new InvalidOperationException());
                    tr.Select(Main.Output.Document.ContentEnd, Main.Output.Document.ContentEnd);
                    tr.Text += $"[{logLevel}] ";

                    // Message Type
                    switch (logLevel)
                    {
                        case LogLevel.Error:
                            tr.ApplyPropertyValue(TextElement.ForegroundProperty,
                                (SolidColorBrush) new BrushConverter().ConvertFromString("#e51400") ??
                                throw new InvalidOperationException());
                            break;

                        case LogLevel.Warning:
                            tr.ApplyPropertyValue(TextElement.ForegroundProperty,
                                (SolidColorBrush) new BrushConverter().ConvertFromString("#f0a30a") ??
                                throw new InvalidOperationException());
                            break;

                        default:
                            tr.ApplyPropertyValue(TextElement.ForegroundProperty,
                                (SolidColorBrush) new BrushConverter().ConvertFromString("#76608a") ??
                                throw new InvalidOperationException());
                            break;
                    }

                    // Message
                    tr.Select(Main.Output.Document.ContentEnd, Main.Output.Document.ContentEnd);
                    tr.Text += message + (newline ? "\n" : string.Empty);
                    tr.ApplyPropertyValue(TextElement.ForegroundProperty,
                        (SolidColorBrush) new BrushConverter().ConvertFromString("White") ??
                        throw new InvalidOperationException());
                });
            }
        }
    }
}