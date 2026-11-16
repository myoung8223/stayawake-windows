using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace StayAwake.Service
{
    /// <summary>Starts a program inside a user's session, as that user, from the SYSTEM service.</summary>
    static class Launcher
    {
        // Owner SYSTEM, so the user gets no implicit right to rewrite the DACL.
        // SYSTEM and Administrators get full access. Authenticated users can only see the process
        // (query limited info + synchronize): no terminate, no memory access, no debugging.
        // Task Manager "End task" and taskkill therefore fail with "Access denied" for standard users.
        const string HardenedSddl = "O:SYG:SYD:(A;;0x1FFFFF;;;SY)(A;;0x1FFFFF;;;BA)(A;;0x101000;;;AU)";

        public static bool Launch(int sessionId, string exePath, string arguments, bool hardened, out int pid)
        {
            pid = 0;
            IntPtr userToken = IntPtr.Zero, primary = IntPtr.Zero, env = IntPtr.Zero, sd = IntPtr.Zero;
            try
            {
                if (!Native.WTSQueryUserToken(sessionId, out userToken))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken");
                if (!Native.DuplicateTokenEx(userToken, Native.MAXIMUM_ALLOWED, IntPtr.Zero, Native.SecurityImpersonation, Native.TokenPrimary, out primary))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx");
                if (!Native.CreateEnvironmentBlock(out env, primary, false))
                    env = IntPtr.Zero; // not fatal

                var si = new Native.STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(Native.STARTUPINFO));
                si.lpDesktop = @"winsta0\default";

                string dir = System.IO.Path.GetDirectoryName(exePath);
                var cmd = new StringBuilder("\"" + exePath + "\"" + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments));
                uint flags = env != IntPtr.Zero ? Native.CREATE_UNICODE_ENVIRONMENT : 0;
                Native.PROCESS_INFORMATION pi;
                bool ok = false;

                if (hardened && Native.ConvertStringSecurityDescriptorToSecurityDescriptorW(HardenedSddl, 1, out sd, IntPtr.Zero))
                {
                    var sa = new Native.SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf(typeof(Native.SECURITY_ATTRIBUTES)), lpSecurityDescriptor = sd };
                    ok = Native.CreateProcessAsUserW(primary, exePath, cmd, ref sa, IntPtr.Zero, false, flags, env, dir, ref si, out pi);
                    if (!ok)
                    {
                        Log.Write("Hardened launch failed (" + new Win32Exception(Marshal.GetLastWin32Error()).Message + "); retrying with default security.");
                        cmd = new StringBuilder("\"" + exePath + "\"" + (string.IsNullOrEmpty(arguments) ? "" : " " + arguments));
                    }
                }
                else
                {
                    pi = default(Native.PROCESS_INFORMATION);
                }

                if (!ok)
                {
                    ok = Native.CreateProcessAsUserNoSa(primary, exePath, cmd, IntPtr.Zero, IntPtr.Zero, false, flags, env, dir, ref si, out pi);
                    if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser");
                }

                pid = pi.dwProcessId;
                Native.CloseHandle(pi.hThread);
                Native.CloseHandle(pi.hProcess);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Launch in session " + sessionId + " failed: " + ex.Message);
                return false;
            }
            finally
            {
                if (sd != IntPtr.Zero) Native.LocalFree(sd);
                if (env != IntPtr.Zero) Native.DestroyEnvironmentBlock(env);
                if (primary != IntPtr.Zero) Native.CloseHandle(primary);
                if (userToken != IntPtr.Zero) Native.CloseHandle(userToken);
            }
        }
    }
}
