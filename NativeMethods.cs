using System.Runtime.InteropServices;

namespace SteamFriendsPatcher
{
    internal class NativeMethods
    {
        [DllImport("msvcrt.dll", EntryPoint = "memcmp", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Memcmp(byte[] b1, byte[] b2, long count);
    }
}