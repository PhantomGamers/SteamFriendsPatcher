﻿using System;
using System.Runtime.InteropServices;

namespace SteamFriendsPatcher
{
    internal class NativeMethods
    {
        public const int HwndBroadcast = 0xffff;
        public static readonly int WmShowme = RegisterWindowMessage("WM_SHOWME");

        [DllImport("msvcrt.dll", EntryPoint = "memcmp", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Memcmp(byte[] b1, byte[] b2, long count);

        [DllImport("user32.dll", EntryPoint = "FindWindow", SetLastError = true, CharSet = CharSet.Unicode,
            ThrowOnUnmappableChar = true)]
        public static extern IntPtr FindWindowByClass(string lpClassName, IntPtr zeroOnly = default);

        [DllImport("user32")]
        public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

        [DllImport("user32", CharSet = CharSet.Unicode, ThrowOnUnmappableChar = true)]
        public static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    }
}