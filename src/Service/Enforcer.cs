using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Win32;
using StayAwake.Shared;

namespace StayAwake.Service
{
    /// <summary>
    /// Decides, once per second, whether each signed-in session must lock.
    ///
    /// The tray agent in each session reports idle time (real keyboard/mouse input) every second.
    /// The service owns the grace periods and the decision, so killing or restarting the agent
    /// cannot extend or cancel anything. Lock escalation when a lock is due:
    ///   1. tell the agent (it calls LockWorkStation)
    ///   2. after 4 s, launch "StayAwake.exe /lock" in the session
    ///   3. after 10 s, disconnect the session, which drops it to the sign-in screen
    /// Outside a grace period, a session whose agent has been silent for 30 s is locked (fail closed).
    /// </summary>
    sealed class Enforcer
    {
        sealed class State
        {
            public int SessionId;
            public DateTime GraceEndUtc = DateTime.MinValue;
            public long GraceTotalMs;
            public DateTime LastHeartbeatUtc = DateTime.MinValue;
            public long ReportedIdleMs;
            public bool Locked = true;
            public DateTime UnlockedSinceUtc = DateTime.MinValue;
            public int LockStage;                 // 0 none, 1 agent asked, 2 helper launched, 3 disconnected
            public DateTime LockRequestedUtc = DateTime.MinValue;
            public DateTime LastLaunchUtc = DateTime.MinValue;
            public bool? Allowed;                 // cached group check, cleared on lock
        }

        static readonly TimeSpan HeartbeatFresh = TimeSpan.FromSeconds(5);
        static readonly TimeSpan RelaunchEvery = TimeSpan.FromSeconds(10);
        static readonly TimeSpan AgentMissingLock = TimeSpan.FromSeconds(30);
        static readonly TimeSpan HelperAfter = TimeSpan.FromSeconds(4);
        static readonly TimeSpan DisconnectAfter = TimeSpan.FromSeconds(10);

        const string HistoryKeyPath = @"SOFTWARE\StayAwake\History";

        readonly object _sync = new object();
        readonly Dictionary<int, State> _states = new Dictionary<int, State>();
        readonly string _agentPath;
        readonly DateTime _startedUtc = DateTime.UtcNow;
        Policy _policy = Policy.Load();
        DateTime _policyLoadedUtc = DateTime.UtcNow;
        Timer _timer;
        volatile bool _quitting;

        public Enforcer(string agentPath) { _agentPath = agentPath; }

        public string AgentPath { get { return _agentPath; } }

        public void Start()
        {
            Log.Write("Policy: idle lock " + _policy.IdleLockMinutes + " min, max grace " + _policy.MaxGraceMinutes +
                      " min, history " + (_policy.MachineHistory ? "machine" : "user") +
                      ", allowed '" + _policy.AllowedGroup + "', ignore injected " + _policy.IgnoreInjectedInput);
            _timer = new Timer(_ => Tick(), null, 1000, 1000);
        }

        /// <summary>Called on session events (logon, unlock, connect) to check sessions right away.</summary>
        public void Poke() { ThreadPool.QueueUserWorkItem(_ => Tick()); }

        /// <summary>Service stopping: tell agents to exit, then end any that remain so files can be replaced.</summary>
        public void Quit()
        {
            _quitting = true;
            if (_timer != null) _timer.Dispose();
            Thread.Sleep(2500); // agents poll every second and exit when they see quit=1
            try
            {
                foreach (Process p in Process.GetProcessesByName("StayAwake"))
                {
                    try
                    {
                        string path = Native.GetProcessPath(p.Id);
                        if (path != null && string.Equals(path, _agentPath, StringComparison.OrdinalIgnoreCase))
                            p.Kill();
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
        }

        void Tick()
        {
            if (_quitting) return;
            if (!Monitor.TryEnter(_sync)) return; // a tick is already running
            try
            {
                DateTime now = DateTime.UtcNow;
                if (now - _policyLoadedUtc > TimeSpan.FromSeconds(10))
                {
                    var fresh = Policy.Load();
                    if (fresh.AllowedGroup != _policy.AllowedGroup)
                        foreach (var s in _states.Values) s.Allowed = null; // re-check membership
                    if (fresh.IdleLockMinutes != _policy.IdleLockMinutes || fresh.MaxGraceMinutes != _policy.MaxGraceMinutes || fresh.AllowedGroup != _policy.AllowedGroup)
                        Log.Write("Policy changed: idle lock " + fresh.IdleLockMinutes + " min, max grace " + fresh.MaxGraceMinutes + " min, allowed '" + fresh.AllowedGroup + "'.");
                    _policy = fresh;
                    _policyLoadedUtc = now;
                }

                var active = Sessions.ActiveUserSessions();
                foreach (int id in active)
                {
                    State st;
                    if (!_states.TryGetValue(id, out st))
                    {
                        st = new State { SessionId = id };
                        _states[id] = st;
                    }
                    Evaluate(st, now);
                }

                // Forget sessions that signed out or disconnected.
                var gone = new List<int>();
                foreach (int id in _states.Keys) if (!active.Contains(id)) gone.Add(id);
                foreach (int id in gone) _states.Remove(id);
            }
            catch (Exception ex)
            {
                Log.Write("Tick error: " + ex);
            }
            finally { Monitor.Exit(_sync); }
        }

        void Evaluate(State st, DateTime now)
        {
            if (Sessions.IsLocked(st.SessionId))
            {
                if (!st.Locked)
                {
                    st.Locked = true;
                    if (st.GraceEndUtc != DateTime.MinValue) Log.Write("Session " + st.SessionId + " locked; grace period cleared.");
                }
                st.GraceEndUtc = DateTime.MinValue;
                st.LockStage = 0;
                st.Allowed = null;
                return;
            }

            if (st.Locked)
            {
                st.Locked = false;
                st.UnlockedSinceUtc = now;
                st.LockStage = 0;
            }

            bool agentAlive = now - st.LastHeartbeatUtc <= HeartbeatFresh;
            if (!agentAlive && now - st.LastLaunchUtc >= RelaunchEvery)
            {
                st.LastLaunchUtc = now;
                int pid;
                if (File.Exists(_agentPath) && Launcher.Launch(st.SessionId, _agentPath, "", true, out pid))
                    Log.Write("Started agent in session " + st.SessionId + " (pid " + pid + ").");
            }

            bool graceActive = st.GraceEndUtc > now;
            if (!graceActive && st.GraceEndUtc != DateTime.MinValue)
            {
                Log.Write("Session " + st.SessionId + " grace period ended.");
                st.GraceEndUtc = DateTime.MinValue;
                st.GraceTotalMs = 0;
            }

            bool lockDue;
            string reason;
            if (graceActive)
            {
                lockDue = false;
                reason = null;
            }
            else if (agentAlive)
            {
                // Idle never counts from before this unlock/sign-in: input on the lock screen
                // may not reach the user's idle clock, and we must not re-lock in a loop.
                long sinceUnlock = (long)(now - st.UnlockedSinceUtc).TotalMilliseconds;
                long idle = Math.Min(st.ReportedIdleMs + (long)(now - st.LastHeartbeatUtc).TotalMilliseconds, sinceUnlock);
                lockDue = idle >= _policy.IdleLockMinutes * 60000L;
                reason = "idle " + (idle / 1000) + " s";
            }
            else
            {
                DateTime since = Max(Max(st.UnlockedSinceUtc, st.LastHeartbeatUtc), _startedUtc);
                lockDue = now - since >= AgentMissingLock;
                reason = "agent not responding";
            }

            if (!lockDue)
            {
                st.LockStage = 0;
                return;
            }

            if (st.LockStage == 0)
            {
                st.LockStage = 1;
                st.LockRequestedUtc = now;
                Log.Write("Locking session " + st.SessionId + " (" + reason + ").");
            }
            else if (st.LockStage == 1 && now - st.LockRequestedUtc >= HelperAfter)
            {
                st.LockStage = 2;
                int pid;
                Launcher.Launch(st.SessionId, _agentPath, "/lock", false, out pid);
                Log.Write("Session " + st.SessionId + " still unlocked; ran lock helper.");
            }
            else if (st.LockStage == 2 && now - st.LockRequestedUtc >= DisconnectAfter)
            {
                st.LockStage = 3;
                bool ok = Native.WTSDisconnectSession(Native.WTS_CURRENT_SERVER_HANDLE, st.SessionId, false);
                Log.Write("Session " + st.SessionId + " still unlocked; disconnect " + (ok ? "succeeded" : "failed") + ".");
            }
            else if (st.LockStage == 3 && now - st.LockRequestedUtc >= TimeSpan.FromSeconds(30))
            {
                st.LockStage = 0; // nothing worked; start the chain again on the next tick
            }
        }

        static DateTime Max(DateTime a, DateTime b) { return a > b ? a : b; }

        // ---- Commands from agents -------------------------------------------

        public string Handle(int sessionId, string line)
        {
            lock (_sync)
            {
                DateTime now = DateTime.UtcNow;
                State st;
                if (!_states.TryGetValue(sessionId, out st))
                {
                    st = new State { SessionId = sessionId, Locked = false, UnlockedSinceUtc = now };
                    _states[sessionId] = st;
                }

                string error = "";
                string[] parts = (line ?? "").Trim().Split(' ');
                switch (parts[0].ToUpperInvariant())
                {
                    case "PING":
                        long idle;
                        if (parts.Length > 1 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out idle) && idle >= 0)
                        {
                            st.ReportedIdleMs = idle;
                            st.LastHeartbeatUtc = now;
                        }
                        break;

                    case "START":
                        error = Start(st, parts, now);
                        break;

                    case "STOP":
                        if (st.GraceEndUtc != DateTime.MinValue) Log.Write("Session " + sessionId + " stopped its grace period.");
                        st.GraceEndUtc = DateTime.MinValue;
                        st.GraceTotalMs = 0;
                        break;

                    default:
                        error = "Unknown command";
                        break;
                }
                return BuildStatus(st, now, error).Serialize();
            }
        }

        string Start(State st, string[] parts, DateTime now)
        {
            if (!IsAllowed(st)) return "Grace periods aren't available for this account";
            if (parts.Length < 3) return "Bad request";
            int max = _policy.MaxGraceMinutes;

            if (string.Equals(parts[1], "MIN", StringComparison.OrdinalIgnoreCase))
            {
                int minutes;
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes) || minutes < 1)
                    return "Enter 1–" + max + " minutes";
                if (minutes > max) return "Maximum is " + max + " minutes";
                st.GraceEndUtc = now.AddMinutes(minutes);
                st.GraceTotalMs = minutes * 60000L;
                if (_policy.MachineHistory) SaveHistory("Minutes", minutes.ToString(CultureInfo.InvariantCulture));
                Log.Write("Session " + st.SessionId + " (" + Sessions.UserName(st.SessionId) + ") started " + minutes + " min grace.");
                return "";
            }

            if (string.Equals(parts[1], "END", StringComparison.OrdinalIgnoreCase))
            {
                int h, m;
                if (!Recents.TryParseHhmm(parts[2], out h, out m)) return "Pick an end time";
                DateTime end = Recents.NextOccurrenceUtc(h, m, now.ToLocalTime());
                double ms = (end - now).TotalMilliseconds;
                if (ms < 60000) return "Pick a time at least 1 minute from now";
                if (ms > max * 60000.0 + 1000) return "That's too far away — max is " + max + " min";
                st.GraceEndUtc = end;
                st.GraceTotalMs = (long)ms;
                if (_policy.MachineHistory) SaveHistory("EndTimes", parts[2]);
                Log.Write("Session " + st.SessionId + " (" + Sessions.UserName(st.SessionId) + ") started grace until " + parts[2] + ".");
                return "";
            }
            return "Bad request";
        }

        bool IsAllowed(State st)
        {
            if (string.IsNullOrWhiteSpace(_policy.AllowedGroup)) return true;
            if (!st.Allowed.HasValue) st.Allowed = Sessions.IsAllowed(st.SessionId, _policy.AllowedGroup);
            return st.Allowed.Value;
        }

        Status BuildStatus(State st, DateTime now, string error)
        {
            var s = new Status
            {
                EndUtcTicks = st.GraceEndUtc > now ? st.GraceEndUtc.Ticks : 0,
                TotalMs = st.GraceEndUtc > now ? st.GraceTotalMs : 0,
                Allowed = IsAllowed(st),
                MaxMinutes = _policy.MaxGraceMinutes,
                IdleMinutes = _policy.IdleLockMinutes,
                Lock = st.LockStage >= 1,
                Quit = _quitting,
                MachineHistory = _policy.MachineHistory,
                Error = error ?? ""
            };
            if (_policy.MachineHistory)
            {
                s.RecentMinutes = LoadHistory("Minutes");
                s.RecentEnds = LoadHistory("EndTimes");
            }
            return s;
        }

        // ---- Machine-wide history (HKLM, written only by the service) --------

        static List<string> LoadHistory(string name)
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(HistoryKeyPath))
                {
                    var v = key == null ? null : key.GetValue(name) as string[];
                    return v == null ? new List<string>() : new List<string>(v);
                }
            }
            catch { return new List<string>(); }
        }

        static void SaveHistory(string name, string value)
        {
            try
            {
                var list = Recents.Push(LoadHistory(name), value);
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.CreateSubKey(HistoryKeyPath))
                    key.SetValue(name, list.ToArray(), RegistryValueKind.MultiString);
            }
            catch (Exception ex) { Log.Write("History save failed: " + ex.Message); }
        }
    }
}
