using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using StayAwake.Shared;

namespace StayAwake.Agent
{
    /// <summary>
    /// The popup window. Same layout and wording as the Chrome extension's popup:
    /// countdown, status line, progress bar, Minutes / End time toggle, entry + Start/Restart,
    /// note line, Recent buttons (3 per row, up to 9), and Stop.
    /// </summary>
    sealed class MainForm : Form
    {
        // ---- Theme (popup.css) ----
        sealed class Theme
        {
            public Color Bg, Fg, Muted, Track, On, Warn, Primary, PrimaryFg, Danger, Btn, BtnHover;
            public static Theme Light()
            {
                return new Theme
                {
                    Bg = Hex(0xffffff), Fg = Hex(0x111827), Muted = Hex(0x6b7280), Track = Hex(0xe5e7eb),
                    On = Hex(0x15803d), Warn = Hex(0xd97706), Primary = Hex(0x2563eb), PrimaryFg = Hex(0xffffff),
                    Danger = Hex(0xdc2626), Btn = Hex(0xf3f4f6), BtnHover = Hex(0xe5e7eb)
                };
            }
            public static Theme Dark()
            {
                var t = Light();
                t.Bg = Hex(0x1f2937); t.Fg = Hex(0xf9fafb); t.Muted = Hex(0x9ca3af); t.Track = Hex(0x374151);
                t.Btn = Hex(0x374151); t.BtnHover = Hex(0x4b5563);
                return t;
            }
            static Color Hex(int v) { return Color.FromArgb((v >> 16) & 0xff, (v >> 8) & 0xff, v & 0xff); }
        }

        sealed class Bar : Control
        {
            public double Fraction;
            public Color Fill, Track;
            public Bar() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float r = Height / 2f;
                using (var tb = new SolidBrush(Track))
                using (var tp = TrayIcons.RoundedRect(new RectangleF(0, 0, Width - 1, Height - 1), r))
                    e.Graphics.FillPath(tb, tp);
                float w = (float)(Width * Math.Max(0, Math.Min(1, Fraction)));
                if (w >= 1)
                    using (var fb = new SolidBrush(Fill))
                    using (var fp = TrayIcons.RoundedRect(new RectangleF(0, 0, Math.Max(w, Height) - 1, Height - 1), r))
                        e.Graphics.FillPath(fb, fp);
            }
        }

        readonly Func<string, Status> _send;
        readonly Action<Status> _statusSink;
        readonly Theme _t = UserSettings.DarkMode ? Theme.Dark() : Theme.Light();
        readonly float _s;

        Label _header, _time, _status, _note, _recentLabel, _unit;
        Bar _bar;
        Panel _seg, _recents;
        Button _modeFor, _modeUntil, _go, _stop;
        NumericUpDown _minutes;
        DateTimePicker _until;
        Timer _tick;

        Status _st;              // last status from the service (null = unreachable)
        bool _untilMode;
        bool _firstStatus = true;
        bool _busy;
        DateTime _errorUntil = DateTime.MinValue;
        string _recentsKey = "";
        public DateTime LastHiddenUtc { get; private set; }

        public MainForm(Func<string, Status> send, Action<Status> statusSink)
        {
            _send = send;
            _statusSink = statusSink;
            AutoScaleMode = AutoScaleMode.None;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) _s = g.DpiX / 96f; // system DPI (the app is system-DPI aware)

            Text = "Stay Awake";
            Icon = TrayIcons.AppIcon();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = _t.Bg;
            ForeColor = _t.Fg;
            KeyPreview = true;
            Font = Px(14);

            Build();
            _untilMode = UserSettings.EndTimeMode;
            _until.Value = DefaultUntil(60);
            SetMode(_untilMode, false);

            _tick = new Timer { Interval = 250 };
            _tick.Tick += (s, e) => Tick();
        }

        int S(float v) { return (int)Math.Round(v * _s); }
        Font Px(float px, FontStyle style = FontStyle.Regular) { return new Font("Segoe UI", px * _s, style, GraphicsUnit.Pixel); }

        // ---------------------------------------------------------------- build

        void Build()
        {
            _header = MakeLabel("Stay Awake", Px(13, FontStyle.Bold), _t.Muted);
            _time = MakeLabel("--:--", Px(48, FontStyle.Bold), _t.Muted);
            _status = MakeLabel("", Px(12), _t.Muted);
            _bar = new Bar { Track = _t.Track, Fill = _t.On };
            Controls.Add(_bar);

            _seg = new Panel { BackColor = _t.Btn };
            _modeFor = MakeButton("Minutes", _t.Btn, _t.Muted, Px(13));
            _modeUntil = MakeButton("End time", _t.Btn, _t.Muted, Px(13));
            _seg.Controls.Add(_modeFor);
            _seg.Controls.Add(_modeUntil);
            Controls.Add(_seg);
            _modeFor.Click += (s, e) => SetMode(false, true);
            _modeUntil.Click += (s, e) => SetMode(true, true);

            _minutes = new NumericUpDown
            {
                Minimum = 1, Maximum = 60, Value = 60, TextAlign = HorizontalAlignment.Center,
                Font = Px(20, FontStyle.Bold), BackColor = _t.Bg, ForeColor = _t.Fg, BorderStyle = BorderStyle.FixedSingle
            };
            _minutes.Enter += (s, e) => _minutes.Select(0, _minutes.Text.Length);
            Controls.Add(_minutes);
            _unit = MakeLabel("min", Px(14), _t.Muted);

            _until = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern,
                ShowUpDown = true,
                Font = Px(17, FontStyle.Bold)
            };
            _until.ValueChanged += (s, e) => { _errorUntil = DateTime.MinValue; Tick(); };
            Controls.Add(_until);

            _go = MakeButton("Start", _t.Primary, _t.PrimaryFg, Px(16, FontStyle.Bold));
            _go.FlatAppearance.MouseOverBackColor = ControlPaint.Light(_t.Primary, 0.2f);
            _go.Click += (s, e) => StartFromEntry();
            AcceptButton = _go;

            _note = MakeLabel("", Px(12), _t.Muted);
            _recentLabel = MakeLabel("RECENT", Px(11), _t.Muted);
            _recentLabel.TextAlign = ContentAlignment.MiddleLeft;
            _recents = new Panel { BackColor = _t.Bg };
            Controls.Add(_recents);

            _stop = MakeButton("Stop", _t.Danger, Color.White, Px(14, FontStyle.Bold));
            _stop.FlatAppearance.MouseOverBackColor = ControlPaint.Light(_t.Danger, 0.2f);
            _stop.Click += (s, e) => RunCommand("STOP", null);
        }

        Label MakeLabel(string text, Font font, Color color)
        {
            var l = new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false };
            Controls.Add(l);
            return l;
        }

        Button MakeButton(string text, Color bg, Color fg, Font font)
        {
            var b = new Button { Text = text, BackColor = bg, ForeColor = fg, Font = font, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Cursor = Cursors.Hand, TabStop = true };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = _t.BtnHover;
            Controls.Add(b);
            return b;
        }

        // ---------------------------------------------------------------- layout

        void DoLayout()
        {
            SuspendLayout();
            int pad = S(16), w = S(260), cw = w - pad * 2, y = S(14);

            _header.SetBounds(pad, y, cw, S(18)); y += S(18) + S(4);
            _time.SetBounds(pad, y, cw, S(62)); y += S(62);
            _status.SetBounds(pad, y, cw, S(18)); y += S(18) + S(10);
            _bar.SetBounds(pad, y, cw, S(6)); y += S(6) + S(14);

            _seg.SetBounds(pad, y, cw, S(34));
            int half = (cw - S(6)) / 2;
            _modeFor.SetBounds(S(3), S(3), half, S(28));
            _modeUntil.SetBounds(S(3) + half, S(3), cw - S(6) - half, S(28));
            y += S(34) + S(8);

            int rowH = S(44);
            if (_untilMode)
            {
                _until.SetBounds(pad, y + (rowH - _until.Height) / 2, S(128), _until.Height);
                int gx = pad + S(128) + S(8);
                _go.SetBounds(gx, y, pad + cw - gx, rowH);
            }
            else
            {
                _minutes.SetBounds(pad, y + (rowH - _minutes.Height) / 2, S(72), _minutes.Height);
                _unit.SetBounds(pad + S(72) + S(4), y, S(32), rowH);
                int gx = pad + S(72) + S(4) + S(32) + S(4);
                _go.SetBounds(gx, y, pad + cw - gx, rowH);
            }
            _minutes.Visible = _unit.Visible = !_untilMode;
            _until.Visible = _untilMode;
            y += rowH + S(5);

            _note.SetBounds(pad, y, cw, S(18)); y += S(18) + S(2);

            int n = _recents.Controls.Count;
            _recentLabel.Visible = _recents.Visible = n > 0;
            if (n > 0)
            {
                y += S(4);
                _recentLabel.SetBounds(pad, y, cw, S(16)); y += S(16) + S(4);
                int gap = S(6), bw = (cw - gap * 2) / 3, bh = S(32);
                for (int i = 0; i < n; i++)
                    _recents.Controls[i].SetBounds((i % 3) * (bw + gap), (i / 3) * (bh + gap), bw, bh);
                int rows = (n + 2) / 3;
                int gh = rows * bh + (rows - 1) * gap;
                _recents.SetBounds(pad, y, cw, gh);
                y += gh;
            }

            if (_stopShown)
            {
                y += S(10);
                _stop.SetBounds(pad, y, cw, S(38));
                y += S(38);
            }
            _laidOutStopShown = _stopShown;
            y += S(12);
            ClientSize = new Size(w, y);
            ResumeLayout();
            // The window can grow while open (first status, a new row of recent buttons, the Stop
            // button appearing). Re-anchor it so it stays fully on screen.
            if (Visible && _screen != null) PlaceNearTray();
        }

        // ---------------------------------------------------------------- state

        int Max { get { return _st != null ? Math.Max(1, _st.MaxMinutes) : 60; } }
        bool Usable { get { return _st != null && _st.Allowed; } }

        List<string> RecentMinutesList { get { return _st != null && _st.MachineHistory ? _st.RecentMinutes : UserSettings.RecentMinutes(); } }
        List<string> RecentEndList { get { return _st != null && _st.MachineHistory ? _st.RecentEnds : UserSettings.RecentEnds(); } }

        /// <summary>Called on the UI thread with each heartbeat result (null = service unreachable).</summary>
        public void ApplyStatus(Status s)
        {
            _st = s;
            if (s != null)
            {
                _minutes.Maximum = Math.Max(1, s.MaxMinutes);
                if (_firstStatus)
                {
                    _firstStatus = false;
                    var rec = RecentMinutesList;
                    int first;
                    _minutes.Value = rec.Count > 0 && int.TryParse(rec[0], out first) ? Clamp(first, 1, Max) : Clamp(60, 1, Max);
                    _until.Value = DefaultUntil(Math.Min(30, Max));
                }
            }
            RenderRecents(false);
            Tick();
        }

        void SetMode(bool untilMode, bool save)
        {
            _untilMode = untilMode;
            StyleSeg(_modeFor, !untilMode);
            StyleSeg(_modeUntil, untilMode);
            _errorUntil = DateTime.MinValue;
            if (save) UserSettings.EndTimeMode = untilMode;
            RenderRecents(true);
            Tick();
            if (Visible) FocusEntry();
        }

        void StyleSeg(Button b, bool selected)
        {
            b.BackColor = selected ? _t.Bg : _t.Btn;
            b.ForeColor = selected ? _t.Fg : _t.Muted;
            b.Font = Px(13, selected ? FontStyle.Bold : FontStyle.Regular);
            b.FlatAppearance.MouseOverBackColor = selected ? _t.Bg : _t.Btn;
        }

        void FocusEntry()
        {
            if (_untilMode) _until.Focus();
            else { _minutes.Focus(); _minutes.Select(0, _minutes.Text.Length); }
        }

        void RenderRecents(bool force)
        {
            var list = _untilMode ? RecentEndList : RecentMinutesList;
            string key = (_untilMode ? "E:" : "M:") + string.Join(",", list) + (Usable ? "" : "!");
            if (!force && key == _recentsKey) { DoLayoutIfNeeded(); return; }
            _recentsKey = key;

            foreach (Control c in _recents.Controls) c.Dispose();
            _recents.Controls.Clear();
            foreach (string v in list)
            {
                var b = new Button { BackColor = _t.Btn, ForeColor = _t.Fg, Font = Px(13), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Cursor = Cursors.Hand };
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = _t.BtnHover;
                string value = v;
                if (_untilMode)
                {
                    int h, m;
                    if (!Recents.TryParseHhmm(v, out h, out m)) continue;
                    b.Text = Clock(DateTime.Today.AddHours(h).AddMinutes(m));
                    b.Tag = v;
                    b.Click += (s, e) => StartUntil(value);
                }
                else
                {
                    b.Text = v + " min";
                    b.Click += (s, e) => { int n; if (int.TryParse(value, out n)) StartMinutes(n); };
                }
                _recents.Controls.Add(b);
            }
            DoLayout();
        }

        // Control.Visible reads false while the form is hidden, so track the intended state separately.
        bool _stopShown, _laidOutStopShown;
        void DoLayoutIfNeeded() { if (_stopShown != _laidOutStopShown) DoLayout(); }

        // ---------------------------------------------------------------- tick

        void Tick()
        {
            DateTime nowUtc = DateTime.UtcNow;
            double left = _st != null && _st.EndUtcTicks > 0 ? (_st.EndUtc - nowUtc).TotalMilliseconds : 0;
            bool on = left > 0;
            bool warn = on && left <= 120000;
            Color accent = warn ? _t.Warn : _t.On;

            _time.Text = on ? Fmt(left) : "--:--";
            _time.ForeColor = on ? accent : _t.Muted;
            if (_st == null) _status.Text = "Waiting for the Stay Awake service…";
            else if (on) _status.Text = "Staying awake until " + Clock(_st.EndUtc.ToLocalTime());
            else if (!_st.Allowed) _status.Text = "Not available for this account";
            else _status.Text = "Off — locks after " + FmtMinutes(_st.IdleMinutes) + " idle";

            double total = _st != null && _st.TotalMs > 0 ? _st.TotalMs : 60000;
            _bar.Fill = accent;
            _bar.Fraction = on ? left / total : 0;
            _bar.Invalidate();

            _go.Text = on ? "Restart" : "Start";
            bool usable = Usable && !_busy;
            _go.Enabled = _modeFor.Enabled = _modeUntil.Enabled = _minutes.Enabled = _until.Enabled = usable;
            _stopShown = on;
            _stop.Visible = on;
            _stop.Enabled = !_busy;

            foreach (Control c in _recents.Controls)
            {
                if (c.Tag is string hhmm)
                {
                    var chk = CheckUntil(hhmm);
                    c.Enabled = usable && chk.Ok;
                }
                else c.Enabled = usable;
            }
            DoLayoutIfNeeded();

            if (DateTime.UtcNow < _errorUntil) return;
            if (_untilMode && Usable)
            {
                var c = CheckUntil(UntilValue);
                _note.Text = c.Text;
                _note.ForeColor = c.Ok ? _t.Muted : _t.Warn;
            }
            else
            {
                _note.Text = "";
                _note.ForeColor = _t.Muted;
            }
        }

        // ---------------------------------------------------------------- actions

        void ShowError(string text)
        {
            _note.Text = text;
            _note.ForeColor = _t.Warn;
            _errorUntil = DateTime.UtcNow.AddMilliseconds(3500);
        }

        void StartFromEntry()
        {
            if (_untilMode) { StartUntil(UntilValue); return; }
            string raw = _minutes.Text.Trim();
            double d;
            if (raw.Length == 0 || !double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out d) || Math.Round(d) < 1)
            {
                ShowError("Enter 1–" + Max + " minutes");
                return;
            }
            int n = (int)Math.Round(d);
            if (n > Max) ShowError("Maximum is " + Max + " minutes");
            StartMinutes(Math.Min(n, Max));
        }

        void StartMinutes(int n)
        {
            n = Clamp(n, 1, Max);
            _minutes.Value = n;
            string v = n.ToString(CultureInfo.InvariantCulture);
            RunCommand("START MIN " + v, () => { if (_st != null && !_st.MachineHistory) UserSettings.PushMinutes(v); });
        }

        void StartUntil(string hhmm)
        {
            var c = CheckUntil(hhmm);
            if (!c.Ok) { ShowError(c.Text); return; }
            int h, m;
            if (Recents.TryParseHhmm(hhmm, out h, out m)) _until.Value = DateTime.Today.AddHours(h).AddMinutes(m);
            RunCommand("START END " + hhmm, () => { if (_st != null && !_st.MachineHistory) UserSettings.PushEnd(hhmm); });
        }

        void RunCommand(string cmd, Action onSuccess)
        {
            if (_busy) return;
            _busy = true;
            Tick();
            Task.Run(() => _send(cmd)).ContinueWith(t =>
            {
                _busy = false;
                Status s = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                if (s == null) { ShowError("The Stay Awake service isn't responding"); Tick(); return; }
                _statusSink(s);
                if (!string.IsNullOrEmpty(s.Error)) { ShowError(s.Error); return; }
                if (onSuccess != null) onSuccess();
                RenderRecents(true);
                Tick();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---------------------------------------------------------------- helpers

        string UntilValue { get { return _until.Value.ToString("HH:mm", CultureInfo.InvariantCulture); } }

        struct UntilResult { public bool Ok; public string Text; }

        UntilResult CheckUntil(string hhmm)
        {
            int h, m;
            if (!Recents.TryParseHhmm(hhmm, out h, out m)) return new UntilResult { Text = "Pick an end time" };
            DateTime now = DateTime.Now;
            DateTime target = Recents.NextOccurrenceUtc(h, m, now).ToLocalTime();
            double ms = (target - now).TotalMilliseconds;
            int mins = (int)Math.Ceiling(ms / 60000);
            if (ms < 60000) return new UntilResult { Text = "Pick a time at least 1 minute from now" };
            if (ms > Max * 60000.0 + 1000) return new UntilResult { Text = "That's " + FmtMinutes(mins) + " away — max is " + Max + " min" };
            return new UntilResult { Ok = true, Text = "Runs " + FmtMinutes(mins) + ", until " + Clock(target) };
        }

        static DateTime DefaultUntil(int minutesAhead)
        {
            DateTime t = DateTime.Now.AddMinutes(minutesAhead);
            t = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0);
            int add = (5 - t.Minute % 5) % 5;
            return t.AddMinutes(add);
        }

        static string Fmt(double ms)
        {
            long s = (long)Math.Ceiling(ms / 1000);
            long h = s / 3600, m = (s % 3600) / 60, sec = s % 60;
            return h > 0 ? string.Format("{0}:{1:00}:{2:00}", h, m, sec) : string.Format("{0}:{1:00}", m, sec);
        }

        static string FmtMinutes(int min)
        {
            if (min < 60) return min + " min";
            int h = min / 60, m = min % 60;
            return m > 0 ? h + " h " + m + " min" : h + " h";
        }

        static string Clock(DateTime local) { return local.ToString("t", CultureInfo.CurrentCulture); }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // ---------------------------------------------------------------- window behavior

        Screen _screen; // the screen the window was opened on

        public void ShowNearTray()
        {
            _screen = Screen.FromPoint(Cursor.Position);
            DoLayout();
            PlaceNearTray();
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
            FocusEntry();
            Tick();
            _tick.Start();
        }

        /// <summary>Puts the window in the corner next to the taskbar and keeps it inside the working area.</summary>
        void PlaceNearTray()
        {
            Rectangle wa = _screen.WorkingArea, b = _screen.Bounds;
            int margin = S(12);
            int x = wa.Right - Width - margin;
            int y = wa.Bottom - Height - margin;               // taskbar at bottom or side: grow upward
            if (wa.Top > b.Top) y = wa.Top + margin;          // taskbar at top: grow downward
            if (wa.Left > b.Left) x = wa.Left + margin;       // taskbar at left
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
            y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height));
            if (Location.X != x || Location.Y != y) Location = new Point(x, y);
        }

        public void HidePopup()
        {
            if (!Visible) return;
            _tick.Stop();
            Hide();
            LastHiddenUtc = DateTime.UtcNow;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HidePopup();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { HidePopup(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Users can't close the program: closing just hides the window.
            if (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.TaskManagerClosing)
            {
                e.Cancel = true;
                HidePopup();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(_t.Track))
                e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW
                cp.ExStyle |= 0x00000080;      // WS_EX_TOOLWINDOW: no Alt+Tab entry
                return cp;
            }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2; // DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND (Windows 11; ignored elsewhere)
                DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
                if (UserSettings.DarkMode)
                {
                    int dark = 1; // DWMWA_USE_IMMERSIVE_DARK_MODE
                    DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
                }
            }
            catch { }
        }
    }
}
