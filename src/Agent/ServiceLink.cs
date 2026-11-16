using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using StayAwake.Shared;

namespace StayAwake.Agent
{
    /// <summary>
    /// Talks to the service over \\.\pipe\StayAwake. A background thread sends a heartbeat with
    /// the idle time every second; UI commands share the same connection. Events fire on the
    /// background thread.
    /// </summary>
    sealed class ServiceLink : IDisposable
    {
        readonly Func<long> _idle;
        readonly object _io = new object();
        readonly Thread _thread;
        volatile bool _disposed;
        NamedPipeClientStream _pipe;
        StreamReader _reader;
        StreamWriter _writer;

        /// <summary>Raised after every heartbeat; the argument is null when the service is unreachable.</summary>
        public event Action<Status> StatusReceived;
        public event Action Disconnected;

        public bool Connected { get { return _pipe != null; } }

        /// <summary>Why the last connection attempt failed (null when connected). Shown in the About box.</summary>
        public volatile string LastError;

        public ServiceLink(Func<long> idleMilliseconds)
        {
            _idle = idleMilliseconds;
            _thread = new Thread(Loop) { IsBackground = true, Name = "ServiceLink" };
        }

        public void Start() { _thread.Start(); }

        void Loop()
        {
            while (!_disposed)
            {
                Status s = Send("PING " + _idle());
                var h = StatusReceived;
                if (h != null) h(s); // null = service unreachable
                Thread.Sleep(1000);
            }
        }

        /// <summary>Sends one command and returns the service's status, or null if the service is unreachable.</summary>
        public Status Send(string command)
        {
            lock (_io)
            {
                if (_disposed) return null;
                if (_pipe == null && !TryConnect()) return null;
                try
                {
                    _writer.WriteLine(command);
                    string reply = _reader.ReadLine();
                    if (reply == null) throw new IOException("Service closed the connection");
                    return Status.Parse(reply);
                }
                catch (Exception)
                {
                    Drop();
                    return null;
                }
            }
        }

        bool TryConnect()
        {
            NamedPipeClientStream pipe = null;
            try
            {
                // Ask for exactly the rights the service grants users. PipeDirection.InOut would request
                // GENERIC_WRITE, which includes CreateNewInstance and is refused.
                pipe = new NamedPipeClientStream(".", Protocol.PipeName,
                    PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.ReadAttributes |
                    PipeAccessRights.WriteAttributes | PipeAccessRights.Synchronize,
                    PipeOptions.None, TokenImpersonationLevel.Identification, HandleInheritability.None);
                pipe.Connect(1000);

                // Only trust a server running in session 0 (a service). A user program can't run there,
                // so it can't pose as the service.
                int session;
                if (!Native.GetNamedPipeServerSessionId(pipe.SafePipeHandle, out session))
                {
                    LastError = "Couldn't read the service's session (error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")";
                    pipe.Dispose();
                    return false;
                }
                if (session != 0)
                {
                    LastError = "Pipe is hosted in session " + session + ", not by the service";
                    pipe.Dispose();
                    return false;
                }

                var utf8 = new UTF8Encoding(false);
                _pipe = pipe;
                _reader = new StreamReader(pipe, utf8, false, 1024, true);
                _writer = new StreamWriter(pipe, utf8, 1024, true) { AutoFlush = true, NewLine = "\n" };
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                if (pipe != null) try { pipe.Dispose(); } catch { }
                return false;
            }
        }

        void Drop()
        {
            try { if (_reader != null) _reader.Dispose(); } catch { }
            try { if (_writer != null) _writer.Dispose(); } catch { }
            try { if (_pipe != null) _pipe.Dispose(); } catch { }
            _pipe = null; _reader = null; _writer = null;
            var h = Disconnected;
            if (h != null) h();
        }

        public void Dispose()
        {
            _disposed = true;
            lock (_io) { if (_pipe != null) Drop(); }
        }
    }
}
