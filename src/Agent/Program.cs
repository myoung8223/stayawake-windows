using System;
using System.Threading;
using System.Windows.Forms;

namespace StayAwake.Agent
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // "StayAwake.exe /lock": the service's fallback when the running agent didn't lock in time.
            foreach (string a in args)
                if (string.Equals(a, "/lock", StringComparison.OrdinalIgnoreCase))
                {
                    Native.LockWorkStation();
                    return;
                }

            bool created;
            using (var mutex = new Mutex(true, @"Local\StayAwakeAgent", out created))
            {
                if (!created) return; // already running in this session

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new AgentContext());
            }
        }
    }
}
