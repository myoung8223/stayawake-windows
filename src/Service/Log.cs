using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace StayAwake.Service
{
    /// <summary>
    /// Small rolling log at C:\ProgramData\StayAwake\service.log (one backup, ~1 MB each).
    /// Admins and SYSTEM can write; users can read.
    /// </summary>
    static class Log
    {
        static readonly object Sync = new object();
        static string _path;

        public static string Directory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StayAwake"); }
        }

        public static void Init()
        {
            try
            {
                string dir = Directory;
                var sec = new DirectorySecurity();
                sec.SetAccessRuleProtection(true, false);
                var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir, sec);
                else System.IO.Directory.SetAccessControl(dir, sec);
                _path = Path.Combine(dir, "service.log");
            }
            catch
            {
                _path = null;
            }
        }

        public static void Write(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
            lock (Sync)
            {
                if (_path == null) return;
                try
                {
                    var fi = new FileInfo(_path);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        string old = _path + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(_path, old);
                    }
                    File.AppendAllText(_path, line, Encoding.UTF8);
                }
                catch { /* logging must never break enforcement */ }
            }
        }
    }
}
