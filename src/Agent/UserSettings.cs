using System;
using System.Collections.Generic;
using Microsoft.Win32;
using StayAwake.Shared;

namespace StayAwake.Agent
{
    /// <summary>Per-user state in HKCU\Software\StayAwake: recent values (when history is per user) and last mode.</summary>
    static class UserSettings
    {
        const string KeyPath = @"Software\StayAwake";

        public static List<string> RecentMinutes() { return ReadList("RecentMinutes"); }
        public static List<string> RecentEnds() { return ReadList("RecentEndTimes"); }
        public static void PushMinutes(string v) { WriteList("RecentMinutes", Recents.Push(RecentMinutes(), v)); }
        public static void PushEnd(string v) { WriteList("RecentEndTimes", Recents.Push(RecentEnds(), v)); }

        public static bool EndTimeMode
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
                        return k != null && string.Equals(k.GetValue("Mode") as string, "EndTime", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
                        k.SetValue("Mode", value ? "EndTime" : "Minutes", RegistryValueKind.String);
                }
                catch { }
            }
        }

        /// <summary>True when Windows apps are set to dark mode.</summary>
        public static bool DarkMode
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                        return k != null && k.GetValue("AppsUseLightTheme") is int v && v == 0;
                }
                catch { return false; }
            }
        }

        static List<string> ReadList(string name)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    var v = k == null ? null : k.GetValue(name) as string[];
                    return v == null ? new List<string>() : new List<string>(v);
                }
            }
            catch { return new List<string>(); }
        }

        static void WriteList(string name, List<string> list)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
                    k.SetValue(name, list.ToArray(), RegistryValueKind.MultiString);
            }
            catch { }
        }
    }
}
