using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace StayAwake.Service
{
    /// <summary>Queries for interactive sessions, their lock state, and their user's group memberships.</summary>
    static class Sessions
    {
        /// <summary>Active (connected, on-screen) sessions that have a signed-in user.</summary>
        public static List<int> ActiveUserSessions()
        {
            var result = new List<int>();
            IntPtr buf;
            int count;
            if (!Native.WTSEnumerateSessionsW(Native.WTS_CURRENT_SERVER_HANDLE, 0, 1, out buf, out count)) return result;
            try
            {
                int size = Marshal.SizeOf(typeof(Native.WTS_SESSION_INFO));
                for (int i = 0; i < count; i++)
                {
                    var info = (Native.WTS_SESSION_INFO)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + i * size), typeof(Native.WTS_SESSION_INFO));
                    if (info.SessionId == 0 || info.State != Native.WTSActive) continue;
                    if (string.IsNullOrEmpty(UserName(info.SessionId))) continue;
                    result.Add(info.SessionId);
                }
            }
            finally { Native.WTSFreeMemory(buf); }
            return result;
        }

        public static string UserName(int sessionId)
        {
            IntPtr buf;
            int bytes;
            if (!Native.WTSQuerySessionInformationW(Native.WTS_CURRENT_SERVER_HANDLE, sessionId, Native.WTSUserName, out buf, out bytes)) return null;
            try { return Marshal.PtrToStringUni(buf); }
            finally { Native.WTSFreeMemory(buf); }
        }

        /// <summary>
        /// True when the session shows the lock screen. Reads WTSINFOEX_LEVEL1.SessionFlags:
        /// the struct is a DWORD Level, 4 bytes of padding (the union holds LARGE_INTEGERs),
        /// then SessionId, SessionState, SessionFlags, so SessionFlags sits at byte offset 16.
        /// </summary>
        public static bool IsLocked(int sessionId)
        {
            IntPtr buf;
            int bytes;
            if (!Native.WTSQuerySessionInformationW(Native.WTS_CURRENT_SERVER_HANDLE, sessionId, Native.WTSSessionInfoEx, out buf, out bytes)) return false;
            try
            {
                if (bytes < 20 || Marshal.ReadInt32(buf, 0) != 1) return false;
                return Marshal.ReadInt32(buf, 16) == Native.WTS_SESSIONSTATE_LOCK;
            }
            finally { Native.WTSFreeMemory(buf); }
        }

        /// <summary>
        /// Whether the session's user may start grace periods. allowed is "" (everyone) or a
        /// semicolon-separated list of groups, users, or SIDs. Unresolvable names never match.
        /// Uses the user's logon token, so group changes take effect at their next sign-in.
        /// </summary>
        public static bool IsAllowed(int sessionId, string allowed)
        {
            if (string.IsNullOrWhiteSpace(allowed)) return true;
            IntPtr token;
            if (!Native.WTSQueryUserToken(sessionId, out token))
            {
                Log.Write("Group check: no token for session " + sessionId);
                return false;
            }
            try
            {
                using (var id = new WindowsIdentity(token))
                {
                    foreach (string raw in allowed.Split(';'))
                    {
                        string name = raw.Trim();
                        if (name.Length == 0) continue;
                        SecurityIdentifier sid = Resolve(name);
                        if (sid == null) continue;
                        if (sid.Equals(id.User)) return true;
                        if (id.Groups != null)
                            foreach (IdentityReference g in id.Groups)
                                if (sid.Equals(g)) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Group check failed: " + ex.Message);
            }
            finally { Native.CloseHandle(token); }
            return false;
        }

        static SecurityIdentifier Resolve(string name)
        {
            try
            {
                if (name.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)) return new SecurityIdentifier(name);
                return (SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier));
            }
            catch (Exception ex)
            {
                Log.Write("AllowedGroup entry '" + name + "' could not be resolved: " + ex.Message);
                return null;
            }
        }
    }
}
