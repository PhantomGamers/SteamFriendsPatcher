using IWshRuntimeLibrary;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public static byte[] Decompress(byte[] gzip)
        {
            using (var stream = new Ionic.Zlib.GZipStream(
                new MemoryStream(gzip),
                Ionic.Zlib.CompressionMode.Decompress, false))
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

        public static byte[] Compress(byte[] raw)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                using (Ionic.Zlib.GZipStream gzip = new Ionic.Zlib.GZipStream(memory,
                    Ionic.Zlib.CompressionMode.Compress, true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }
                return memory.ToArray();
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

        public static byte[] GetCRC(string gzipFilePath)
        {
            byte[] crc = new byte[4];

            using (BinaryReader reader = new BinaryReader(new FileStream(gzipFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
            {
                reader.BaseStream.Seek(-4, SeekOrigin.End);
                reader.Read(crc, 0, 4);
            }

            return crc;
        }

        public static bool CompareCRC(string filePath, byte[] bArr)
        {

            return ByteArrayCompare(GetCRC(filePath), bArr.Skip(bArr.Length - 4).Take(4).ToArray());
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