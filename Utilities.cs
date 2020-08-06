using IWshRuntimeLibrary;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace SteamFriendsPatcher
{
    internal class Utilities
    {

        public static bool IsGZipHeader(IReadOnlyList<byte> arr)
        {
            return arr.Count >= 2 &&
                   arr[0] == 31 &&
                   arr[1] == 139;
        }

        public static IEnumerable<byte> Decompress(byte[] gzip)
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



        public static bool ByteArrayCompare(byte[] b1, byte[] b2)
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
                Program.Print("Could not create startup shortcut.", Program.LogLevel.Error);
                return;
            }

            shortcut.TargetPath = Assembly.GetExecutingAssembly().Location;
            shortcut.WorkingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            shortcut.IconLocation = Assembly.GetExecutingAssembly().Location;
            shortcut.Save();
        }


        // Link to startup file
        public static readonly string StartupLink = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs\Startup",
            Assembly.GetExecutingAssembly().GetName().Name + ".lnk");

        public static readonly string StartupLinkOld = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs\Startup",
            Assembly.GetExecutingAssembly().GetName().Name + ".url");
    }
}
