using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Media;

namespace SteamFriendsPatcher
{
    internal class Program
    {
        // location of steam directory
        private static string steamDir = FindSteamDir();

        // location of Steam's CEF Cache
        private static string steamCacheDir = Path.Combine(Environment.GetEnvironmentVariable("LocalAppData"), "Steam\\htmlcache\\Cache\\");

        // current language set on steam
        private static string steamLang = FindSteamLang();

        // location of file containing translations for set language
        private static string steamLangFile = steamDir + "\\friends\\trackerui_" + steamLang + ".txt";

        // title of friends window in set language
        private static readonly string FriendsString = FindFriendsListString();

        // friends.css etag
        private static string etag = null;

        // original friends.css
        public static byte[] friendscss;

        // patched friends.css
        public static byte[] friendscsspatched;

        // original friends.css age
        public static DateTime friendscssage;

        // FileSystemWatchers
        public static FileSystemWatcher cacheWatcher;

        public static FileSystemWatcher crashWatcher;
        public static FileStream cacheLock;
        public static bool scannerExists = false;

        // objects to lock to maintain thread safety
        private static readonly object MessageLock = new object();

        private static readonly object ScannerLock = new object();
        private static readonly object UpdateScannerLock = new object();
        private static readonly object GetFriendsCSSLock = new object();
        private static readonly object ToggleScannerButtonLock = new object();
        private static readonly object ToggleForceScannerButtonLock = new object();

        public static bool UpdateChecker()
        {
            lock (UpdateScannerLock)
            {
                Print("Checking for updates...");
                try
                {
                    WebClient wc = new WebClient();
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                    wc.Headers.Add("user-agent", Assembly.GetExecutingAssembly().FullName);

                    string latestver = wc.DownloadString("https://api.github.com/repos/phantomgamers/steamfriendspatcher/releases/latest");
                    string verregex = "(?<=\"tag_name\":\")(.*?)(?=\")";
                    string latestvervalue = Regex.Match(latestver, verregex).Value;
                    if (!string.IsNullOrEmpty(latestvervalue))
                    {
                        Version localver = new Version(ThisAssembly.AssemblyInformationalVersion);
                        Version remotever = new Version(latestvervalue);
                        if (remotever > localver)
                        {
                            if (System.Windows.Forms.MessageBox.Show("Update available. Download now?", "Steam Friends Patcher - Update Available", System.Windows.Forms.MessageBoxButtons.YesNo) == System.Windows.Forms.DialogResult.Yes)
                            {
                                Process.Start("https://github.com/PhantomGamers/SteamFriendsPatcher/releases/latest");
                            }

                            return true;
                        }
                        else
                        {
                            Print("No updates found.");
                            return false;
                        }
                    }
                    Print("Failed to check for updates.", "Error");
                    return false;
                }
                catch (WebException we)
                {
                    Print("Failed to check for updates.", "Error");
                    Print(we.ToString(), "Error");
                }
                return false;
            }
        }

        private static void PatchCacheFile(string friendscachefile, byte[] decompressedcachefile)
        {
            Print("Adding import line to friends.css...");
            decompressedcachefile = PrependFile(decompressedcachefile);

            Print("Recompressing friends.css...");
            using (FileStream file = new FileStream(friendscachefile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (GZipStream gzip = new GZipStream(file, CompressionLevel.Optimal, false))
            {
                Print("Overwriting original friends.css...");
                gzip.Write(decompressedcachefile, 0, decompressedcachefile.Length);
            }

            if (!File.Exists(steamDir + "\\clientui\\friends.custom.css"))
            {
                File.Create(steamDir + "\\clientui\\friends.custom.css").Dispose();
            }

            if (Process.GetProcessesByName("Steam").Length > 0 && File.Exists(steamDir + "\\clientui\\friends.custom.css") && FindFriendsWindow())
            {
                Print("Reloading friends window...");
                Process.Start(steamDir + "\\Steam.exe", @"steam://friends/status/offline");
                Task.Delay(1000).Wait();
                Process.Start(steamDir + "\\Steam.exe", @"steam://friends/status/online");
            }

            Print("Done! Put your custom css in " + steamDir + "\\clientui\\friends.custom.css", "Success");
            Print("Close and reopen your Steam friends window to see changes.", "Success");
        }

        public static void FindCacheFile(bool forceUpdate = false)
        {
            bool preScannerStatus = scannerExists;
            ToggleCacheScanner(false);
            ToggleForceScanButtonEnabled(false);
            ToggleScanButtonEnabled(false);
            ToggleClearCacheButtonEnabled(false);

            Print("Force scan started.");
            GetLatestFriendsCSS(forceUpdate);
            Print("Finding list of possible cache files...");
            if (!Directory.Exists(steamCacheDir))
            {
                Print("Cache folder does not exist.", "error");
                Print("Please confirm that Steam is running and that the friends list is open and try again.", "error");
                goto ResetButtons;
            }
            var validFiles = new DirectoryInfo(steamCacheDir).EnumerateFiles("f_*", SearchOption.TopDirectoryOnly)
                .Where(f => f.Length <= friendscss.Length)
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => f.FullName)
                .ToList();
            if (validFiles.Count() == 0)
            {
                Print("No cache files found.", "Error");
                Print("Please confirm that Steam is running and that the friends list is open and try again.", "Error");
                goto ResetButtons;
            }
            Print($"Found {validFiles.Count} possible cache files.");

            string friendscachefile = null;
            bool patchedFileFound = false;

            Print("Checking cache files for match...");
            Parallel.ForEach(validFiles, (s, state) =>
            {
                byte[] cachefile;

                try
                {
                    using (FileStream f = new FileStream(s, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        cachefile = new byte[f.Length];
                        f.Read(cachefile, 0, cachefile.Length);
                    }
                }
                catch
                {
                    Print($"Error, {s} could not be opened.", "Debug");
                    return;
                }

                if (IsGZipHeader(cachefile))
                {
                    byte[] decompressedcachefile = Decompress(cachefile);
                    if (decompressedcachefile.Length == friendscss.Length && ByteArrayCompare(decompressedcachefile, friendscss))
                    {
                        state.Stop();
                        Print($"Successfully found matching friends.css at {s}.", "Success");
                        File.WriteAllBytes(steamDir + "\\clientui\\friends.original.css", Encoding.ASCII.GetBytes("/*" + etag + "*/\n").Concat(decompressedcachefile).ToArray());
                        friendscachefile = s;
                        PatchCacheFile(s, decompressedcachefile);
                        return;
                    }
                    else if (decompressedcachefile.Length == friendscsspatched.Length && ByteArrayCompare(decompressedcachefile, friendscsspatched))
                    {
                        patchedFileFound = true;
                    }
                }
            });

            if (string.IsNullOrEmpty(friendscachefile))
            {
                if (!patchedFileFound)
                {
                    Print("Cache file does not exist or is outdated.", "Warning");
                }
                else
                {
                    Print("Cache file is already patched.");
                }
                goto ResetButtons;
            }

        ResetButtons:
            ToggleForceScanButtonEnabled(true);
            ToggleScanButtonEnabled(true);
            ToggleClearCacheButtonEnabled(true);
            if (preScannerStatus) { ToggleCacheScanner(true); }
            return;
        }

        private static byte[] PrependFile(byte[] file)
        {
            // custom only
            // string appendText = "@import url(\"https://steamloopback.host/friends.custom.css\");\n";

            // custom overrides original (!important tags not needed)
            string appendText = "@import url(\"https://steamloopback.host/friends.original.css\");\n@import url(\"https://steamloopback.host/friends.custom.css\");\n{";

            // original overrides custom (!important tags needed, this is the original behavior)
            // string appendText = "@import url(\"https://steamloopback.host/friends.custom.css\");\n@import url(\"https://steamloopback.host/friends.original.css\");\n{";

            // load original from Steam CDN, not recommended because of infinite matching
            // string appendText = "@import url(\"https://steamcommunity-a.akamaihd.net/public/css/webui/friends.css\");\n@import url(\"https://steamloopback.host/friends.custom.css\");\n";
            byte[] append = Encoding.ASCII.GetBytes(appendText);

            byte[] output = append.Concat(file).Concat(Encoding.ASCII.GetBytes("}")).ToArray();
            return output;
        }

        public static bool GetLatestFriendsCSS(bool force = false)
        {
            if (DateTime.Now.Subtract(friendscssage).TotalMinutes < 5.0 && !force)
            {
                return true;
            }
            lock (GetFriendsCSSLock)
            {
                Print("Downloading latest friends.css...");
                try
                {
                    WebClient wc = new WebClient();
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

                    string steamChat = wc.DownloadString("https://steam-chat.com/chat/clientui/?l=&build=&cc");
                    string eTagRegex = "(?<=<link href=\"https:\\/\\/steamcommunity-a.akamaihd.net\\/public\\/css\\/webui\\/friends.css\\?v=)(.*?)(?=\")";
                    etag = Regex.Match(steamChat, eTagRegex).Value;
                    byte[] fc;
                    if (!string.IsNullOrEmpty(etag))
                    {
                        fc = wc.DownloadData("https://steamcommunity-a.akamaihd.net/public/css/webui/friends.css?v=" + etag);
                        if (fc.Length > 0)
                        {
                            friendscss = fc;
                            friendscsspatched = PrependFile(fc);
                            friendscssage = DateTime.Now;
                            Print("Successfully downloaded latest friends.css", "Success");
                            return true;
                        }
                        Print("Failed to download friends.css", "Error");
                        return false;
                    }
                    else
                    {
                        Print("Could not find etag, downloading default css.", "Debug");
                        fc = wc.DownloadData("https://steamcommunity-a.akamaihd.net/public/css/webui/friends.css");
                        if (fc.Length > 0)
                        {
                            friendscss = fc;
                            friendscsspatched = PrependFile(fc);
                            friendscssage = DateTime.Now;
                            Print("Successfully downloaded latest friends.css.", "Success");
                            return true;
                        }
                        Print("Failed to download friends.css.", "Error");
                        return false;
                    }
                }
                catch (WebException we)
                {
                    Print("Failed to download friends.css.", "Error");
                    Print(we.ToString(), "Error");
                    return false;
                }
            }
        }

        private static string FindSteamDir()
        {
            using (var registryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam"))
            {
                string filePath = null;
                var regFilePath = registryKey?.GetValue("SteamPath");
                if (regFilePath != null)
                {
                    filePath = regFilePath.ToString().Replace(@"/", @"\");
                }

                return filePath;
            }
        }

        private static string FindSteamLang()
        {
            using (var registryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam"))
            {
                return registryKey?.GetValue("Language").ToString();
            }
        }

        private static string FindFriendsListString()
        {
            string regex = "(?<=\"Friends_InviteInfo_FriendsList\"\\t{1,}\")(.*?)(?=\")";
            string s = null;
            string tracker = null;
            string smatch = null;
            if (File.Exists(steamLangFile))
            {
                tracker = File.ReadAllText(steamLangFile);
            }

            if (!string.IsNullOrEmpty(tracker))
            {
                smatch = Regex.Match(tracker, regex).Value;
            }

            if (!string.IsNullOrEmpty(smatch))
            {
                s = smatch;
            }

            return s;
        }

        private static bool FindFriendsWindow()
        {
            if (!string.IsNullOrEmpty(FriendsString) && WindowSearch.FindWindowLike.Find(0, FriendsString, "SDL_app").Count() > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static void ToggleCacheScanner(bool isEnabled)
        {
            lock (ScannerLock)
            {
                DirectoryInfo cacheDir;
                if (!Directory.Exists(steamCacheDir))
                {
                    cacheDir = Directory.CreateDirectory(steamCacheDir);
                }
                else
                {
                    cacheDir = new DirectoryInfo(steamCacheDir);
                }

                if (scannerExists)
                {
                    cacheWatcher.EnableRaisingEvents = isEnabled;
                    crashWatcher.EnableRaisingEvents = isEnabled;
                    string buttonText = isEnabled ? "Stop Scanning" : "Start Scanning";
                    ToggleScanButtonEnabled(true, buttonText);
                    Print("Cache Watcher " + (isEnabled ? "Started" : "Stopped") + ".");
                    scannerExists = isEnabled;
                    if (!isEnabled)
                    {
                        cacheLock.Dispose();
                        if (File.Exists(Path.Combine(steamCacheDir, "tmp.lock")))
                        {
                            File.Delete(Path.Combine(steamCacheDir, "tmp.lock"));
                        }
                    }
                    return;
                }
                else if (!isEnabled)
                {
                    return;
                }

                while (!cacheDir.Exists) Task.Delay(20).Wait();
                cacheLock = new FileStream(Path.Combine(steamCacheDir, "tmp.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                ToggleScanButtonEnabled(false, "Stop Scanning");
                ToggleForceScanButtonEnabled(false);
                ToggleClearCacheButtonEnabled(false);

                StartCrashScanner();

                cacheWatcher = new FileSystemWatcher
                {
                    Path = steamCacheDir,
                    NotifyFilter = NotifyFilters.LastAccess
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.FileName,
                    Filter = "f_*"
                };
                cacheWatcher.Created += new FileSystemEventHandler(CacheWatcher_Created);
                cacheWatcher.Changed += new FileSystemEventHandler(CacheWatcher_Created);
                cacheWatcher.EnableRaisingEvents = true;
                scannerExists = true;
                Print("Cache Watcher Started.");

                ToggleScanButtonEnabled(true);
                ToggleForceScanButtonEnabled(true);
                ToggleClearCacheButtonEnabled(true);
            }
            return;
        }

        private static void CacheWatcher_Created(object sender, FileSystemEventArgs e)
        {
            Print($"New file found: {e.Name}", "Debug");
            byte[] cachefile, decompressedcachefile;

            try
            {
                using (FileStream f = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    cachefile = new byte[f.Length];
                    f.Read(cachefile, 0, cachefile.Length);
                }
            }
            catch
            {
                Task.Delay(2000).Wait();
                if (File.Exists(e.FullPath))
                {
                    Print($"Error opening file {e.Name}, retrying.", "Debug");
                    CacheWatcher_Created(sender, e);
                }
                return;
            }

            if (!IsGZipHeader(cachefile))
            {
                Print($"{e.Name} not a gzip file.", "Debug");
                return;
            }

            decompressedcachefile = Decompress(cachefile);

            if (decompressedcachefile.Length == friendscss.Length &&
                ByteArrayCompare(decompressedcachefile, friendscss))
            {
                Print($"Found match in {e.Name}");
                PatchCacheFile(e.FullPath, decompressedcachefile);
            }
            else
            {
                Print($"{e.Name} did not match.", "Debug");
            }
            return;
        }

        private static void StartCrashScanner()
        {
            if (!Directory.Exists(steamDir))
            {
                Print("Steam directory not found.", "Warning");
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
            crashWatcher.Created += new FileSystemEventHandler(CrashWatcher_Event);
            crashWatcher.Changed += new FileSystemEventHandler(CrashWatcher_Event);

            crashWatcher.EnableRaisingEvents = true;

            Print("Crash scanner started.", "Debug");
        }

        private static void CrashWatcher_Event(object sender, FileSystemEventArgs e)
        {
            Print("Steam start detected.", "Debug");
            GetLatestFriendsCSS();
        }

        public static void ClearSteamCache()
        {
            if (!Directory.Exists(steamCacheDir))
            {
                Print("Cache folder does not exist.", "Warning");
                return;
            }
            bool preScannerStatus = scannerExists;
            bool preSteamStatus = Process.GetProcessesByName("Steam").Length > 0;
            if (preSteamStatus)
            {
                if (System.Windows.Forms.MessageBox.Show("Steam will need to be restarted to clear cache. Restart automatically?", "Steam Friends Patcher", System.Windows.Forms.MessageBoxButtons.YesNo) != System.Windows.Forms.DialogResult.Yes)
                {
                    return;
                }
                Print("Shutting down Steam...");
                Process.Start(steamDir + "\\Steam.exe", "-shutdown");
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (Process.GetProcessesByName("Steam").Length > 0 && stopwatch.Elapsed.Seconds < 10)
                {
                    Task.Delay(20).Wait();
                }

                stopwatch.Stop();
                if (Process.GetProcessesByName("Steam").Length > 0)
                {
                    Print("Could not successfully shutdown Steam, please manually shutdown Steam and try again.", "error");
                    goto ResetButtons;
                }
            }

            ToggleCacheScanner(false);
            ToggleForceScanButtonEnabled(false);
            ToggleScanButtonEnabled(false);
            ToggleClearCacheButtonEnabled(false);

            Print("Deleting cache files...");
            try
            {
                Directory.Delete(steamCacheDir, true);
            }
            catch (IOException ioe)
            {
                Print("Some cache files in use, cannot delete.", "Error");
                Print(ioe.ToString(), "Error");
            }

            ToggleCacheScanner(preScannerStatus);
            if (preSteamStatus)
            {
                Print("Restarting Steam...");
                Process.Start(steamDir + "\\Steam.exe");
            }

        ResetButtons:
            ToggleForceScanButtonEnabled(true);
            ToggleScanButtonEnabled(true);
            ToggleClearCacheButtonEnabled(true);
            return;
        }

        internal static byte[] Decompress(byte[] gzip)
        {
            // Create a GZIP stream with decompression mode.
            // ... Then create a buffer and write into while reading from the GZIP stream.
            using (GZipStream stream = new GZipStream(
                new MemoryStream(gzip),
                CompressionMode.Decompress))
            {
                const int size = 4096;
                byte[] buffer = new byte[size];
                using (MemoryStream memory = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = stream.Read(buffer, 0, size);
                        if (count > 0)
                        {
                            memory.Write(buffer, 0, count);
                        }
                    }
                    while (count > 0);
                    return memory.ToArray();
                }
            }
        }

        internal static bool IsGZipHeader(byte[] arr)
        {
            return arr.Length >= 2 &&
                arr[0] == 31 &&
                arr[1] == 139;
        }

        internal static bool ByteArrayCompare(byte[] b1, byte[] b2)
        {
            // Validate buffers are the same length.
            // This also ensures that the count does not exceed the length of either buffer.
            return b1.Length == b2.Length && Memcmp(b1, b2, b1.Length) == 0;
        }

        private static void ToggleScanButtonEnabled(bool status, string text = null)
        {
            lock (ToggleScannerButtonLock)
            {
                MainWindow.scanButtonRef.Dispatcher.Invoke((MethodInvoker)delegate { MainWindow.scanButtonRef.Content = text ?? MainWindow.scanButtonRef.Content; });
                MainWindow.scanButtonRef.Dispatcher.Invoke((MethodInvoker)delegate { MainWindow.scanButtonRef.IsEnabled = status; });
            }
        }

        private static void ToggleForceScanButtonEnabled(bool status)
        {
            lock (ToggleForceScannerButtonLock)
            {
                MainWindow.forceScanButtonRef.Dispatcher.Invoke((MethodInvoker)delegate { MainWindow.forceScanButtonRef.IsEnabled = status; });
            }
        }

        private static void ToggleClearCacheButtonEnabled(bool status)
        {
            lock (ToggleForceScannerButtonLock)
            {
                MainWindow.clearCacheButtonRef.Dispatcher.Invoke((MethodInvoker)delegate { MainWindow.clearCacheButtonRef.IsEnabled = status; });
            }
        }

        public static void Print(string message = null, string messagetype = "Info", bool newline = true)
        {
#if DEBUG
            Debug.Write(message + Environment.NewLine);
#endif
            if (messagetype == "Debug")
            {
                return;
            }
            lock (MessageLock)
            {
                MainWindow.outputRef.Dispatcher.Invoke((MethodInvoker)delegate
                {
                    TextRange tr;
                    // Date & Time
                    tr = new TextRange(MainWindow.outputRef.Document.ContentEnd, MainWindow.outputRef.Document.ContentEnd)
                    {
                        Text = $"[{DateTime.Now}] "
                    };
                    tr.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)new BrushConverter().ConvertFromString("#76608a"));

                    // Message Type
                    switch (messagetype)
                    {
                        case "Error":
                            MainWindow.outputRef.Selection.Select(MainWindow.outputRef.Document.ContentEnd, MainWindow.outputRef.Document.ContentEnd);
                            MainWindow.outputRef.Selection.Text = "[ERROR] ";
                            MainWindow.outputRef.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)new BrushConverter().ConvertFromString("#e51400"));
                            break;

                        case "Warning":
                            MainWindow.outputRef.Selection.Select(MainWindow.outputRef.Document.ContentEnd, MainWindow.outputRef.Document.ContentEnd);
                            MainWindow.outputRef.Selection.Text = "[WARNING] ";
                            MainWindow.outputRef.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)new BrushConverter().ConvertFromString("#f0a30a"));
                            break;

                        case "Success":
                            MainWindow.outputRef.Selection.Select(MainWindow.outputRef.Document.ContentEnd, MainWindow.outputRef.Document.ContentEnd);
                            MainWindow.outputRef.Selection.Text = "[SUCCESS] ";
                            MainWindow.outputRef.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)new BrushConverter().ConvertFromString("#60a917"));
                            break;

                        default:
                            MainWindow.outputRef.Selection.Select(MainWindow.outputRef.Document.ContentEnd, MainWindow.outputRef.Document.ContentEnd);
                            MainWindow.outputRef.Selection.Text = "[INFO] ";
                            MainWindow.outputRef.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)new BrushConverter().ConvertFromString("#76608a"));
                            break;
                    }

                    // Message
                    MainWindow.outputRef.Selection.Select(MainWindow.outputRef.Document.ContentEnd, MainWindow.outputRef.Document.ContentEnd);
                    MainWindow.outputRef.Selection.Text = message + (newline ? "\n" : string.Empty);
                    MainWindow.outputRef.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)new BrushConverter().ConvertFromString("#ffffff"));

                    MainWindow.outputRef.ScrollToEnd();
                });
            }
        }

        [DllImport("msvcrt.dll", EntryPoint = "memcmp", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Memcmp(byte[] b1, byte[] b2, long count);
    }
}