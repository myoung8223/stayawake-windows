using System;
using System.Threading;
using System.Windows.Forms;
using StayAwake.Shared;

namespace StayAwake.Agent
{
    /// <summary>
    /// The tray agent: tray icon, popup window, hotkey, idle monitor, and the link to the service.
    /// There is deliberately no Exit command. The agent exits only when the service tells it to
    /// (service stopping for upgrade or uninstall).
    /// </summary>
    sealed class AgentContext : ApplicationContext
    {
        readonly SynchronizationContext _ui;
        readonly NotifyIcon _tray;
        readonly MainForm _form;
        readonly IdleMonitor _idle;
        readonly ServiceLink _link;
        readonly Hotkey _hotkey;
        readonly Policy _policy;
        IntPtr _iconHandle = IntPtr.Zero;
        string _iconKey = "";
        DateTime _lastLockCall = DateTime.MinValue;
        bool _exiting;

        public AgentContext()
        {
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _policy = Policy.Load();

            _idle = new IdleMonitor(_policy.IgnoreInjectedInput);
            _link = new ServiceLink(() => _idle.IdleMilliseconds);
            _form = new MainForm(cmd => _link.Send(cmd), s => OnStatus(s, true));
            _form.CreateControl();
            var force = _form.Handle; // create the window now so status updates can be marshaled to it

            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Stay Awake", null, (s, e) => ShowForm());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("About Stay Awake", null, (s, e) => About());

            _tray = new NotifyIcon { Text = "Stay Awake", ContextMenuStrip = menu, Visible = true };
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
            UpdateTray(null);

            _hotkey = new Hotkey(_policy.Hotkey);
            _hotkey.Pressed += (s, e) => Toggle();

            _link.StatusReceived += s => _ui.Post(_ => OnStatus(s, false), null);
            _link.Start();
        }

        void OnStatus(Status s, bool fromCommand)
        {
            if (_exiting) return;
            if (s != null && s.Quit) { Exit(); return; }

            if (s != null && s.Lock && DateTime.UtcNow - _lastLockCall > TimeSpan.FromSeconds(3))
            {
                _lastLockCall = DateTime.UtcNow;
                _form.HidePopup();
                Native.LockWorkStation();
            }

            _form.ApplyStatus(s);
            UpdateTray(s);
        }

        void UpdateTray(Status s)
        {
            int? minutes = null;
            string tip = "Stay Awake — off";
            if (s == null) tip = "Stay Awake — waiting for service";
            else if (s.EndUtcTicks > 0)
            {
                double left = (s.EndUtc - DateTime.UtcNow).TotalMinutes;
                if (left > 0)
                {
                    minutes = (int)Math.Ceiling(left);
                    tip = "Stay Awake — " + minutes + " min left (until " + s.EndUtc.ToLocalTime().ToString("t") + ")";
                }
            }
            else if (s != null) tip = "Stay Awake — off (locks after " + s.IdleMinutes + " min idle)";

            if (tip.Length > 63) tip = tip.Substring(0, 63); // NotifyIcon limit
            _tray.Text = tip;

            bool dark = UserSettings.DarkMode;
            string key = (minutes.HasValue ? minutes.Value.ToString() : "off") + (dark ? "d" : "l");
            if (key == _iconKey) return;
            _iconKey = key;

            IntPtr h = TrayIcons.Create(minutes, dark);
            _tray.Icon = System.Drawing.Icon.FromHandle(h);
            if (_iconHandle != IntPtr.Zero) Native.DestroyIcon(_iconHandle);
            _iconHandle = h;
        }

        void Toggle()
        {
            if (_form.Visible) { _form.HidePopup(); return; }
            // A click on the tray icon first deactivates (and hides) the open window; don't reopen it.
            if (DateTime.UtcNow - _form.LastHiddenUtc < TimeSpan.FromMilliseconds(300)) return;
            ShowForm();
        }

        void ShowForm() { _form.ShowNearTray(); }

        void About()
        {
            string version = typeof(AgentContext).Assembly.GetName().Version.ToString(3);
            string hotkey = _hotkey.Registered ? _policy.Hotkey : (_hotkey.Error ?? "none");
            MessageBox.Show(
                "Stay Awake " + version + "\n\n" +
                "Locks this PC after " + _policy.IdleLockMinutes + " min without keyboard or mouse input.\n" +
                "Start a grace period (up to " + _policy.MaxGraceMinutes + " min) to keep it unlocked while you present.\n\n" +
                "Hotkey: " + hotkey + "\n" +
                "Service: " + (_link.Connected ? "connected" : "not connected" + (_link.LastError != null ? " (" + _link.LastError + ")" : "")) + "\n\n" +
                "Developed by Mike Young with AI assistance from Anthropic Claude.",
                "About Stay Awake", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void Exit()
        {
            if (_exiting) return;
            _exiting = true;
            _link.Dispose();
            _hotkey.Dispose();
            _idle.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            if (_iconHandle != IntPtr.Zero) Native.DestroyIcon(_iconHandle);
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            _exiting = true;
            _tray.Visible = false;
            base.ExitThreadCore();
        }
    }
}
