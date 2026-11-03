using Microsoft.Win32;

namespace UltraWideScreenShare.WinForms
{
    public static class Autostart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "UltraWideScreenShare2";
        public const string TrayArgument = "--tray";

        private static string Command => $"\"{Environment.ProcessPath}\" {TrayArgument}";

        public static bool IsEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
            }
        }

        public static void Enable()
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(ValueName, Command);
        }

        public static void Disable()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        /// <summary>
        /// Keeps the registered path up to date, e.g. after the exe was moved or replaced by a new release.
        /// </summary>
        public static void RefreshPath()
        {
            if (IsEnabled)
                Enable();
        }
    }
}
