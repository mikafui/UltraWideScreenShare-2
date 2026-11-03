using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace UltraWideScreenShare.WinForms
{
    public static class GlobalHotkey
    {
        public const int WM_HOTKEY = 0x0312;
        public const int HotkeyId = 1;

        /// <summary>
        /// Registers a system wide hotkey such as "Win+J" or "Ctrl+Alt+J" for the given window.
        /// </summary>
        public static bool Register(IntPtr windowHandle, string hotkey)
        {
            if (!TryParse(hotkey, out var modifiers, out var key))
                return false;

            return PInvoke.RegisterHotKey(new HWND(windowHandle), HotkeyId, modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT, (uint)key);
        }

        public static void Unregister(IntPtr windowHandle) => PInvoke.UnregisterHotKey(new HWND(windowHandle), HotkeyId);

        private static bool TryParse(string hotkey, out HOT_KEY_MODIFIERS modifiers, out Keys key)
        {
            modifiers = 0;
            key = Keys.None;
            foreach (var part in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (part.ToLowerInvariant())
                {
                    case "win": modifiers |= HOT_KEY_MODIFIERS.MOD_WIN; break;
                    case "ctrl": case "control": modifiers |= HOT_KEY_MODIFIERS.MOD_CONTROL; break;
                    case "alt": modifiers |= HOT_KEY_MODIFIERS.MOD_ALT; break;
                    case "shift": modifiers |= HOT_KEY_MODIFIERS.MOD_SHIFT; break;
                    default:
                        if (!Enum.TryParse(part, ignoreCase: true, out key)) return false;
                        break;
                }
            }
            return key != Keys.None;
        }
    }
}
