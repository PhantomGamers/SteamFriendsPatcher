using System;
using System.Collections.Generic;
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
using System.Windows.Threading;

namespace SteamFriendsPatcher
{
    class Program
    {
        public static bool scannerActive = false;

        // location of steam directory
        private static string steamDir = FindSteamDir();

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

        // original friends.css age
        public static DateTime friendscssage;

        // objects to lock to maintain thread safety
        private static readonly object MessageLock = new object();
        private static readonly object ScannerLock = new object();
        private static readonly object UpdateScannerLock = new object();
        private static readonly object GetFriendsCSSLock = new object();
        private static readonly object ToggleScannerButtonLock = new object();
        private static readonly object ToggleForceScannerButtonLock = new object();

        private static bool UpdateChecker()
        {
            return false;
            lock (UpdateScannerLock)
            {
                Print("Checking for updates...");
                try
                {
                    WebClient wc = new WebClient();
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                    wc.Headers.Add("user-agent", Assembly.GetExecutingAssembly().FullName);

                    string latestver = wc.DownloadString("https://api.github.com/repos/phantomgamers/enablenewsteamfriendsskin/releases/latest");
                    string verregex = "(?<=\"tag_name\":\")(.*?)(?=\")";
                    string latestvervalue = Regex.Match(latestver, verregex).Value;
                    if (!string.IsNullOrEmpty(latestvervalue))
                    {
                        Version localver = Assembly.GetExecutingAssembly().GetName().Version;
                        Version remotever = new Version(latestvervalue);
                        if (remotever > localver)
                        {
                            if (System.Windows.Forms.MessageBox.Show("Update available. Download now?", "Steam Friends Patcher - Update Available", System.Windows.Forms.MessageBoxButtons.YesNo) == System.Windows.Forms.DialogResult.Yes)
                            {
                                Process.Start("https://github.com/PhantomGamers/EnableNewSteamFriendsSkin/releases/latest");
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
                    Print($"WebException: {we}");
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
            bool preScannerStatus = scannerActive;
            scannerActive = false;
            ToggleForceScanButtonEnabled(false);
            ToggleScanButtonEnabled(false);

            string cachepath = Path.Combine(Environment.GetEnvironmentVariable("LocalAppData"), "Steam\\htmlcache\\Cache\\");
            Print("Force scan started.");
            GetLatestFriendsCSS(forceUpdate);
            Print("Finding list of possible cache files...");
            if (!Directory.Exists(cachepath))
            {
                Print("Cache folder does not exist.", "error");
                Print("Please confirm that Steam is running and that the friends list is open and try again.", "error");
                goto ResetButtons;
            }
            var validFiles = new DirectoryInfo(cachepath).EnumerateFiles("f_*", SearchOption.TopDirectoryOnly)
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
                }
            });

            if (string.IsNullOrEmpty(friendscachefile))
            {
                Print("Friends.css location not found. Cache file does not exist or is already patched.", "Error");
                goto ResetButtons;
            }

        ResetButtons:
            ToggleForceScanButtonEnabled(true);
            ToggleScanButtonEnabled(true);
            if (preScannerStatus)
            {
                StartCacheScannerTaskAsync();
            }
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
                            Print("Successfully downloaded latest friends.css.", "Success");
                            friendscssage = DateTime.Now;
                            return true;
                        }
                        Print("Failed to download friends.css.", "Error");
                        return false;
                    }
                }
                catch (WebException we)
                {
                    Print("Failed to download friends.css.", "Error");
                    Print($"WebException: {we}");
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

        private static void StartCacheScanner(bool firstRun = false)
        {
            lock (ScannerLock)
            {
                if (scannerActive)
                {
                    return;
                }

                if (!firstRun)
                {
                    ToggleScanButtonEnabled(false, "Stop Scanning");
                    ToggleForceScanButtonEnabled(false);
                }

                scannerActive = true;

                StartCrashScannerTaskAsync();

                using (FileSystemWatcher watcher = new FileSystemWatcher())
                {
                    watcher.Path = Path.Combine(Environment.GetEnvironmentVariable("LocalAppData"), "Steam\\htmlcache\\Cache\\");
                    watcher.NotifyFilter = NotifyFilters.LastAccess
                                         | NotifyFilters.LastWrite
                                         | NotifyFilters.FileName;
                    watcher.Filter = "f_*";
                    watcher.Created += new FileSystemEventHandler(CacheWatcher_Created);

                    watcher.EnableRaisingEvents = true;

                    Print("Watcher started.");

                    GetLatestFriendsCSS();

                    ToggleScanButtonEnabled(true);
                    ToggleForceScanButtonEnabled(true);

                    while (scannerActive) Task.Delay(500).Wait();

                    ToggleScanButtonEnabled(false);
                    ToggleForceScanButtonEnabled(false);

                    watcher.EnableRaisingEvents = false;
                    watcher.Created -= new FileSystemEventHandler(CacheWatcher_Created);
                    watcher.Dispose();

                    Print("Watcher stopped.");
                    ToggleScanButtonEnabled(true, "Start Scanning");
                    ToggleForceScanButtonEnabled(true);
                }
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
            using (FileSystemWatcher watcher = new FileSystemWatcher())
            {
                watcher.Path = steamDir;
                watcher.NotifyFilter = NotifyFilters.LastAccess
                                     | NotifyFilters.LastWrite
                                     | NotifyFilters.FileName;
                watcher.Filter = ".crash";
                watcher.Created += new FileSystemEventHandler(CrashWatcher_Event);
                watcher.Changed += new FileSystemEventHandler(CrashWatcher_Event);

                watcher.EnableRaisingEvents = true;

                Print("Crash scanner started.", "Debug");

                while (scannerActive) Task.Delay(500).Wait();

                watcher.EnableRaisingEvents = false;
                watcher.Created -= new FileSystemEventHandler(CrashWatcher_Event);
                watcher.Changed -= new FileSystemEventHandler(CrashWatcher_Event);
                watcher.Dispose();

                Print("Crash scanner stopped.", "Debug");
            }
        }

        private static void CrashWatcher_Event(object sender, FileSystemEventArgs e)
        {
            Print("Steam start detected.");
            GetLatestFriendsCSS(true);
        }

        public static async void StartCrashScannerTaskAsync()
        {
            await Task.Run(() => StartCrashScanner());
        }

        public static async void StartCheckForUpdateTaskAsync()
        {
            await Task.Run(() => UpdateChecker());
        }

        public static async void StartCacheScannerTaskAsync()
        {
            await Task.Run(() => StartCacheScanner());
        }

        public static async void StartForceScanTaskAsync(bool forceUpdate = false)
        {
            await Task.Run(() => FindCacheFile(forceUpdate));
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
                //if (!MainWindow.scanButton.Dispatcher.CheckAccess())
                //{
                    MainWindow.scanButton.Dispatcher.Invoke((MethodInvoker)delegate { MainWindow.scanButton.Content = text ?? MainWindow.scanButton.Content; });
                    MainWindow.scanButton.Dispatcher.Invoke((MethodInvoker)delegate { MainWindow.scanButton.IsEnabled = status; });
                //}
                //else
                //{
                //    MainWindow.scanButton.Content = text ?? MainWindow.scanButton.Content;
                //    MainWindow.scanButton.IsEnabled = status;
                //}
            }
        }
        private static void ToggleForceScanButtonEnabled(bool status)
        {
            lock (ToggleForceScannerButtonLock)
            {
                //if (!MainWindow.scanButton.Dispatcher.CheckAccess())
                //{
                    MainWindow.forceScanButton.Dispatcher.Invoke((MethodInvoker)delegate { MainWindow.forceScanButton.IsEnabled = status; });
                //}
                //else
                //{
                //    MainWindow.forceScanButton.IsEnabled = status;
                //}
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
                MainWindow.outputRTB.Dispatcher.Invoke((MethodInvoker)delegate {
                    TextRange tr;
                    // Date & Time
                    tr = new TextRange(MainWindow.outputRTB.Document.ContentEnd, MainWindow.outputRTB.Document.ContentEnd)
                    {
                        Text = $"[{DateTime.Now}] "
                    };
                    tr.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)new BrushConverter().ConvertFromString("#76608a"));

                    // Message Type
                    switch (messagetype)
                    {
                        case "Error":
                            MainWindow.outputRTB.Selection.Select(MainWindow.outputRTB.Document.ContentEnd, MainWindow.outputRTB.Document.ContentEnd);
                            MainWindow.outputRTB.Selection.Text = "[ERROR] ";
                            MainWindow.outputRTB.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)new BrushConverter().ConvertFromString("#e51400"));
                            /*
                            tr = new TextRange(MainWindow.outputRTB.Document.ContentEnd, MainWindow.outputRTB.Document.ContentEnd)
                            {
                                Text = "[ERROR] "
                            };
                            tr.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)new BrushConverter().ConvertFromString("#e51400"));
                            */break;
                        case "Warning":
                            MainWindow.outputRTB.Selection.Select(MainWindow.outputRTB.Document.ContentEnd, MainWindow.outputRTB.Document.ContentEnd);
                            MainWindow.outputRTB.Selection.Text = "[WARNING] ";
                            MainWindow.outputRTB.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)new BrushConverter().ConvertFromString("#f0a30a"));
                            break;
                        case "Success":
                            MainWindow.outputRTB.Selection.Select(MainWindow.outputRTB.Document.ContentEnd, MainWindow.outputRTB.Document.ContentEnd);
                            MainWindow.outputRTB.Selection.Text = "[SUCCESS] ";
                            MainWindow.outputRTB.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)new BrushConverter().ConvertFromString("#60a917"));
                            break;
                        default:
                            MainWindow.outputRTB.Selection.Select(MainWindow.outputRTB.Document.ContentEnd, MainWindow.outputRTB.Document.ContentEnd);
                            MainWindow.outputRTB.Selection.Text = "[INFO] ";
                            MainWindow.outputRTB.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)new BrushConverter().ConvertFromString("#76608a"));
                            break;
                    }

                    // Message
                    MainWindow.outputRTB.Selection.Select(MainWindow.outputRTB.Document.ContentEnd, MainWindow.outputRTB.Document.ContentEnd);
                    MainWindow.outputRTB.Selection.Text = message + (newline ? "\n" : string.Empty);
                    MainWindow.outputRTB.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, (SolidColorBrush)new BrushConverter().ConvertFromString("#ffffff"));

                    MainWindow.outputRTB.ScrollToEnd();
                });
            }
        }

        [DllImport("msvcrt.dll", EntryPoint = "memcmp", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Memcmp(byte[] b1, byte[] b2, long count);
    }
}
