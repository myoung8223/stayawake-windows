using System;
using System.Windows.Forms;

namespace StayAwake.Agent
{
    /// <summary>
    /// Global hotkey from the policy string, e.g. "Ctrl+Alt+S", "Win+Alt+K", "Ctrl+Shift+F9".
    /// "None" or an empty string disables it. Windows reserves most Win+letter combinations;
    /// if registration fails the tray icon still opens the window.
    /// </summary>
    sealed class Hotkey : NativeWindow, IDisposable
    {
        const int Id = 0x5341; // "SA"
        static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        public event EventHandler Pressed;
        public bool Registered { get; private set; }
        public string Error { get; private set; }

        public Hotkey(string spec)
        {
            int mods, vk;
            if (!TryParse(spec, out mods, out vk, out string error))
            {
                Error = error;
                return;
            }
            CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
            Registered = Native.RegisterHotKey(Handle, Id, mods | Native.MOD_NOREPEAT, vk);
            if (!Registered) Error = "\"" + spec + "\" is in use by Windows or another program";
        }

        public static bool TryParse(string spec, out int mods, out int vk, out string error)
        {
            mods = 0; vk = 0; error = null;
            if (string.IsNullOrWhiteSpace(spec) || spec.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                error = "Hotkey disabled";
                return false;
            }
            foreach (string raw in spec.Split('+'))
            {
                string t = raw.Trim();
                switch (t.ToUpperInvariant())
                {
                    case "CTRL": case "CONTROL": mods |= Native.MOD_CONTROL; continue;
                    case "ALT": mods |= Native.MOD_ALT; continue;
                    case "SHIFT": mods |= Native.MOD_SHIFT; continue;
                    case "WIN": case "WINDOWS": mods |= Native.MOD_WIN; continue;
                }
                if (vk != 0) { error = "More than one key in \"" + spec + "\""; return false; }
                if (t.Length == 1 && char.IsLetterOrDigit(t[0])) { vk = char.ToUpperInvariant(t[0]); continue; }
                Keys k;
                if (Enum.TryParse(t, true, out k) && (k & Keys.Modifiers) == 0) { vk = (int)k; continue; }
                error = "Unknown key \"" + t + "\"";
                return false;
            }
            if (vk == 0) { error = "No key in \"" + spec + "\""; return false; }
            if (mods == 0) { error = "A hotkey needs at least one of Ctrl, Alt, Shift, Win"; return false; }
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == Id)
            {
                var h = Pressed;
                if (h != null) h(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                if (Registered) Native.UnregisterHotKey(Handle, Id);
                DestroyHandle();
            }
        }
    }
}
