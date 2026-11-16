using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StayAwake.Agent
{
    static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool LockWorkStation();

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO { public int cbSize; public uint dwTime; }

        [DllImport("user32.dll")]
        public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;
        public const int LLKHF_INJECTED = 0x10;
        public const int LLMHF_INJECTED = 0x01;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string name);

        public const int WM_HOTKEY = 0x0312;
        public const int MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int modifiers, int vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        // Reads the server's session from the pipe itself. (ProcessIdToSessionId would have to open
        // the SYSTEM service process, which a standard user isn't allowed to do.)
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetNamedPipeServerSessionId(SafePipeHandle pipe, out int sessionId);
    }
}
