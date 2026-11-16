using System;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace StayAwake.Service
{
    sealed class StayAwakeService : ServiceBase
    {
        public const string Name = "StayAwakeService";

        Enforcer _enforcer;
        PipeServer _pipes;

        public StayAwakeService()
        {
            ServiceName = Name;
            CanStop = true;
            CanShutdown = true;
            CanHandleSessionChangeEvent = true;
            CanPauseAndContinue = false;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            Log.Init();
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            string agent = Path.GetFullPath(Path.Combine(dir, "StayAwake.exe"));
            Log.Write("Service starting, version " + typeof(StayAwakeService).Assembly.GetName().Version + ", agent " + agent);

            ConfigureRecovery();
            _enforcer = new Enforcer(agent);
            _pipes = new PipeServer(_enforcer);
            _pipes.Start();
            _enforcer.Start();
        }

        protected override void OnStop()
        {
            Log.Write("Service stopping.");
            if (_enforcer != null) _enforcer.Quit();
            if (_pipes != null) _pipes.Stop();
        }

        protected override void OnShutdown()
        {
            Log.Write("System shutting down.");
            if (_pipes != null) _pipes.Stop();
        }

        protected override void OnSessionChange(SessionChangeDescription change)
        {
            switch (change.Reason)
            {
                case SessionChangeReason.SessionLogon:
                case SessionChangeReason.SessionUnlock:
                case SessionChangeReason.ConsoleConnect:
                case SessionChangeReason.RemoteConnect:
                    if (_enforcer != null) _enforcer.Poke();
                    break;
            }
        }

        /// <summary>Restart the service 5 seconds after any failure, with no limit.</summary>
        static void ConfigureRecovery()
        {
            IntPtr scm = Native.OpenSCManagerW(null, null, Native.SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return;
            IntPtr svc = Native.OpenServiceW(scm, Name, Native.SERVICE_CHANGE_CONFIG | Native.SERVICE_START);
            IntPtr actions = IntPtr.Zero;
            try
            {
                if (svc == IntPtr.Zero) return;
                int size = Marshal.SizeOf(typeof(Native.SC_ACTION));
                actions = Marshal.AllocHGlobal(size * 3);
                for (int i = 0; i < 3; i++)
                    Marshal.StructureToPtr(new Native.SC_ACTION { Type = Native.SC_ACTION_RESTART, Delay = 5000 }, new IntPtr(actions.ToInt64() + i * size), false);
                var sfa = new Native.SERVICE_FAILURE_ACTIONS { dwResetPeriod = 86400, cActions = 3, lpsaActions = actions };
                if (!Native.ChangeServiceConfig2(svc, Native.SERVICE_CONFIG_FAILURE_ACTIONS, ref sfa))
                    Log.Write("Could not set recovery actions (error " + Marshal.GetLastWin32Error() + ").");
                var flag = new Native.SERVICE_FAILURE_ACTIONS_FLAG { fFailureActionsOnNonCrashFailures = true };
                Native.ChangeServiceConfig2(svc, Native.SERVICE_CONFIG_FAILURE_ACTIONS_FLAG, ref flag);
            }
            finally
            {
                if (actions != IntPtr.Zero) Marshal.FreeHGlobal(actions);
                if (svc != IntPtr.Zero) Native.CloseServiceHandle(svc);
                Native.CloseServiceHandle(scm);
            }
        }
    }

    static class Program
    {
        static void Main()
        {
            ServiceBase.Run(new StayAwakeService());
        }
    }
}
