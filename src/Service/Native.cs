using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace StayAwake.Service
{
    static class Native
    {
        // ---- WTS ---------------------------------------------------------
        public static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;
        public const int WTSActive = 0;
        public const int WTSUserName = 5;
        public const int WTSSessionInfoEx = 25;
        public const int WTS_SESSIONSTATE_LOCK = 0;
        public const int WTS_SESSIONSTATE_UNLOCK = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WTS_SESSION_INFO
        {
            public int SessionId;
            public IntPtr pWinStationName;
            public int State;
        }

        [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool WTSEnumerateSessionsW(IntPtr hServer, int reserved, int version, out IntPtr ppSessionInfo, out int count);

        [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool WTSQuerySessionInformationW(IntPtr hServer, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

        [DllImport("wtsapi32.dll")]
        public static extern void WTSFreeMemory(IntPtr memory);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSQueryUserToken(int sessionId, out IntPtr token);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSDisconnectSession(IntPtr hServer, int sessionId, bool wait);

        // ---- Tokens / processes -------------------------------------------
        public const uint MAXIMUM_ALLOWED = 0x02000000;
        public const int SecurityImpersonation = 2;
        public const int TokenPrimary = 1;
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint PROCESS_TERMINATE = 0x0001;
        public const uint SYNCHRONIZE = 0x00100000;

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public bool bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr newToken);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcessAsUserW(IntPtr token, string applicationName, StringBuilder commandLine,
            ref SECURITY_ATTRIBUTES processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
            IntPtr environment, string currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessAsUserW")]
        public static extern bool CreateProcessAsUserNoSa(IntPtr token, string applicationName, StringBuilder commandLine,
            IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags,
            IntPtr environment, string currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

        [DllImport("userenv.dll", SetLastError = true)]
        public static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        public static extern bool DestroyEnvironmentBlock(IntPtr environment);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, int revision, out IntPtr sd, IntPtr sdSize);

        [DllImport("kernel32.dll")]
        public static extern IntPtr LocalFree(IntPtr mem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ProcessIdToSessionId(int pid, out int sessionId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetNamedPipeClientSessionId(SafePipeHandle pipe, out int sessionId);

        // ---- Service configuration ----------------------------------------
        public const int SC_MANAGER_CONNECT = 0x0001;
        public const int SERVICE_CHANGE_CONFIG = 0x0002;
        public const int SERVICE_START = 0x0010;
        public const int SERVICE_CONFIG_FAILURE_ACTIONS = 2;
        public const int SERVICE_CONFIG_FAILURE_ACTIONS_FLAG = 4;
        public const int SC_ACTION_RESTART = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct SC_ACTION { public int Type; public int Delay; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SERVICE_FAILURE_ACTIONS
        {
            public int dwResetPeriod;
            public string lpRebootMsg;
            public string lpCommand;
            public int cActions;
            public IntPtr lpsaActions;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_FAILURE_ACTIONS_FLAG { public bool fFailureActionsOnNonCrashFailures; }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenSCManagerW(string machine, string database, int access);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenServiceW(IntPtr scm, string name, int access);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "ChangeServiceConfig2W")]
        public static extern bool ChangeServiceConfig2(IntPtr service, int infoLevel, ref SERVICE_FAILURE_ACTIONS info);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "ChangeServiceConfig2W")]
        public static extern bool ChangeServiceConfig2(IntPtr service, int infoLevel, ref SERVICE_FAILURE_ACTIONS_FLAG info);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CloseServiceHandle(IntPtr handle);

        // ---- Helpers -------------------------------------------------------
        public static string GetProcessPath(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
            }
            finally { CloseHandle(h); }
        }
    }
}
