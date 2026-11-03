using System;
using System.Windows.Forms;

namespace UltraWideScreenShare.WinForms
{
    internal static class Program
    {
        private const string InstanceMutexName = @"Local\UltraWideScreenShare2.Instance";
        private const string ShowEventName = @"Local\UltraWideScreenShare2.Show";

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            using var instanceMutex = new Mutex(true, InstanceMutexName, out var isFirstInstance);
            using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            if (!isFirstInstance)
            {
                // already running (e.g. in the tray) - bring the existing window to front instead
                showEvent.Set();
                return;
            }

            if (Environment.OSVersion.Version.Major >= 6)
                SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(true);

            var startInTray = args.Contains(Autostart.TrayArgument, StringComparer.OrdinalIgnoreCase);
            var mainWindow = new MainWindow(startInTray);
            ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) =>
            {
                if (mainWindow.IsHandleCreated)
                    mainWindow.BeginInvoke(mainWindow.ShowFromTray);
            }, null, Timeout.Infinite, executeOnlyOnce: false);

            Application.Run(mainWindow);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }
}
