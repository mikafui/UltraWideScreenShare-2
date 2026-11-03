namespace UltraWideScreenShare.WinForms
{
    /// <summary>
    /// Small log file next to settings.json to find out what happens when monitors change.
    /// </summary>
    public static class Log
    {
        private const long MaxSize = 512 * 1024;
        private static readonly string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UltraWideScreenShare2", "log.txt");

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                if (File.Exists(_logPath) && new FileInfo(_logPath).Length > MaxSize)
                    File.Move(_logPath, _logPath + ".old", overwrite: true);
                File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch
            {
                // logging is best effort
            }
        }
    }
}
