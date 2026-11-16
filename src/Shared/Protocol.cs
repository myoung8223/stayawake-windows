using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StayAwake.Shared
{
    /// <summary>
    /// Line-based protocol between the tray agent and the service over \\.\pipe\StayAwake.
    /// The agent sends one command line and the service answers with one status line.
    ///
    ///   PING &lt;idleMs&gt;          heartbeat, sent every second with the user's idle time
    ///   START MIN &lt;minutes&gt;     start (or restart) a grace period by duration
    ///   START END &lt;hh:mm&gt;      start (or restart) a grace period ending at a local clock time
    ///   STOP                    end the grace period
    ///
    /// Reply: OK|key=value|key=value...
    /// </summary>
    public static class Protocol
    {
        public const string PipeName = "StayAwake";
        public const int RecentCount = 9;
    }

    public sealed class Status
    {
        public long EndUtcTicks;       // 0 = no grace period
        public long TotalMs;           // full length of the current grace period, for the progress bar
        public bool Allowed = true;    // false = account not in AllowedGroup
        public int MaxMinutes = 60;
        public int IdleMinutes = 10;
        public bool Lock;              // service wants the session locked now
        public bool Quit;              // service is stopping (uninstall/upgrade); agent should exit
        public bool MachineHistory;
        public List<string> RecentMinutes = new List<string>();
        public List<string> RecentEnds = new List<string>();
        public string Error = "";

        public DateTime EndUtc { get { return EndUtcTicks == 0 ? DateTime.MinValue : new DateTime(EndUtcTicks, DateTimeKind.Utc); } }

        public string Serialize()
        {
            var sb = new StringBuilder("OK");
            Add(sb, "end", EndUtcTicks.ToString(CultureInfo.InvariantCulture));
            Add(sb, "total", TotalMs.ToString(CultureInfo.InvariantCulture));
            Add(sb, "allowed", Allowed ? "1" : "0");
            Add(sb, "max", MaxMinutes.ToString(CultureInfo.InvariantCulture));
            Add(sb, "idle", IdleMinutes.ToString(CultureInfo.InvariantCulture));
            Add(sb, "lock", Lock ? "1" : "0");
            Add(sb, "quit", Quit ? "1" : "0");
            Add(sb, "scope", MachineHistory ? "Machine" : "User");
            Add(sb, "hmin", string.Join(",", RecentMinutes));
            Add(sb, "hend", string.Join(",", RecentEnds));
            Add(sb, "err", Clean(Error));
            return sb.ToString();
        }

        public static Status Parse(string line)
        {
            var s = new Status();
            if (string.IsNullOrEmpty(line) || !line.StartsWith("OK", StringComparison.Ordinal)) { s.Error = "Bad reply"; return s; }
            foreach (string part in line.Split('|'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string k = part.Substring(0, eq), v = part.Substring(eq + 1);
                switch (k)
                {
                    case "end": long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out s.EndUtcTicks); break;
                    case "total": long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out s.TotalMs); break;
                    case "allowed": s.Allowed = v == "1"; break;
                    case "max": int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out s.MaxMinutes); break;
                    case "idle": int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out s.IdleMinutes); break;
                    case "lock": s.Lock = v == "1"; break;
                    case "quit": s.Quit = v == "1"; break;
                    case "scope": s.MachineHistory = v == "Machine"; break;
                    case "hmin": s.RecentMinutes = SplitList(v); break;
                    case "hend": s.RecentEnds = SplitList(v); break;
                    case "err": s.Error = v; break;
                }
            }
            return s;
        }

        static void Add(StringBuilder sb, string k, string v) { sb.Append('|').Append(k).Append('=').Append(v); }

        static string Clean(string s) { return (s ?? "").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' '); }

        static List<string> SplitList(string v)
        {
            var list = new List<string>();
            foreach (string x in v.Split(',')) if (x.Length > 0) list.Add(x);
            return list;
        }
    }

    public static class Recents
    {
        /// <summary>Moves value to the front, removes duplicates, keeps at most Protocol.RecentCount.</summary>
        public static List<string> Push(IEnumerable<string> existing, string value)
        {
            var list = new List<string> { value };
            if (existing != null)
                foreach (string x in existing)
                    if (x != value && list.Count < Protocol.RecentCount) list.Add(x);
            return list;
        }

        /// <summary>Validates "HH:mm" (24-hour) and returns hours and minutes.</summary>
        public static bool TryParseHhmm(string v, out int h, out int m)
        {
            h = m = 0;
            if (string.IsNullOrEmpty(v) || v.Length != 5 || v[2] != ':') return false;
            return int.TryParse(v.Substring(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out h)
                && int.TryParse(v.Substring(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out m)
                && h >= 0 && h < 24 && m >= 0 && m < 60;
        }

        /// <summary>Next local occurrence of hh:mm (today, or tomorrow if already past), as UTC.</summary>
        public static DateTime NextOccurrenceUtc(int h, int m, DateTime nowLocal)
        {
            var t = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, h, m, 0, DateTimeKind.Local);
            if (t <= nowLocal) t = t.AddDays(1);
            return t.ToUniversalTime();
        }
    }
}
