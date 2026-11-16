using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using StayAwake.Shared;

namespace StayAwake.Service
{
    /// <summary>
    /// Serves \\.\pipe\StayAwake. Only the installed StayAwake.exe may talk to the service:
    /// each client's process image path is checked, so a look-alike program copied elsewhere
    /// (for example one that always reports "not idle") is disconnected.
    /// Users get read/write on the pipe but cannot create server instances of it.
    /// </summary>
    sealed class PipeServer
    {
        readonly Enforcer _enforcer;
        readonly List<NamedPipeServerStream> _open = new List<NamedPipeServerStream>();
        Thread _acceptThread;
        volatile bool _stopping;

        public PipeServer(Enforcer enforcer) { _enforcer = enforcer; }

        public void Start()
        {
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "PipeAccept" };
            _acceptThread.Start();
        }

        public void Stop()
        {
            _stopping = true;
            try
            {
                // Unblock WaitForConnection with a throwaway connection.
                using (var c = new NamedPipeClientStream(".", Protocol.PipeName, PipeDirection.InOut))
                    c.Connect(500);
            }
            catch { }
            lock (_open)
            {
                foreach (var p in _open) { try { p.Dispose(); } catch { } }
                _open.Clear();
            }
        }

        public const PipeAccessRights ClientRights =
            PipeAccessRights.ReadData | PipeAccessRights.WriteData |
            PipeAccessRights.ReadAttributes | PipeAccessRights.WriteAttributes |
            PipeAccessRights.ReadExtendedAttributes | PipeAccessRights.ReadPermissions |
            PipeAccessRights.Synchronize;

        static PipeSecurity BuildSecurity()
        {
            var ps = new PipeSecurity();
            ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            // Users may connect, read, and write. They get no CreateNewInstance, so they can't host the pipe themselves.
            // Synchronize and the attribute rights are required by CreateFile on the client side.
            ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), ClientRights, AccessControlType.Allow));
            return ps;
        }

        void AcceptLoop()
        {
            var security = BuildSecurity();
            while (!_stopping)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(Protocol.PipeName, PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                        PipeOptions.None, 4096, 4096, security);
                    pipe.WaitForConnection();
                    if (_stopping) { pipe.Dispose(); break; }
                    lock (_open) _open.Add(pipe);
                    var p = pipe;
                    new Thread(() => Serve(p)) { IsBackground = true, Name = "PipeClient" }.Start();
                }
                catch (Exception ex)
                {
                    if (pipe != null) try { pipe.Dispose(); } catch { }
                    if (_stopping) break;
                    Log.Write("Pipe accept error: " + ex.Message);
                    Thread.Sleep(1000);
                }
            }
        }

        void Serve(NamedPipeServerStream pipe)
        {
            try
            {
                int pid, sessionId;
                if (!Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out pid) || !Native.GetNamedPipeClientSessionId(pipe.SafePipeHandle, out sessionId))
                {
                    Log.Write("Pipe client identity unavailable (error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ").");
                    return;
                }
                string path = Native.GetProcessPath(pid);
                if (path == null || !string.Equals(path, _enforcer.AgentPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!_stopping) Log.Write("Rejected pipe client pid " + pid + " (" + (path ?? "unknown") + ") in session " + sessionId + ".");
                    return;
                }

                var utf8 = new UTF8Encoding(false);
                using (var reader = new StreamReader(pipe, utf8, false, 1024, true))
                using (var writer = new StreamWriter(pipe, utf8, 1024, true) { AutoFlush = true, NewLine = "\n" })
                {
                    string line;
                    while (!_stopping && (line = reader.ReadLine()) != null)
                    {
                        if (line.Length > 512) break;
                        writer.WriteLine(_enforcer.Handle(sessionId, line));
                    }
                }
            }
            catch (IOException) { /* client went away */ }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { Log.Write("Pipe client error: " + ex.Message); }
            finally
            {
                lock (_open) _open.Remove(pipe);
                try { pipe.Dispose(); } catch { }
            }
        }
    }
}
