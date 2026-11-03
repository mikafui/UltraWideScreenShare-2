using System.Text.Json;

namespace UltraWideScreenShare.WinForms
{
    public class WindowPlacement
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Maximized { get; set; }

        public Rectangle Bounds => new Rectangle(Left, Top, Width, Height);

        public bool IsVisibleOnAnyScreen() => Screen.AllScreens.Any(s =>
        {
            var intersection = Rectangle.Intersect(s.WorkingArea, Bounds);
            return intersection.Width >= 100 && intersection.Height >= 50;
        });
    }

    public class AppSettings
    {
        private static readonly string _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UltraWideScreenShare2", "settings.json");

        /// <summary>
        /// Window placement per monitor layout, so switching monitors (e.g. via a KVM switch)
        /// doesn't overwrite the position of another layout.
        /// </summary>
        public Dictionary<string, WindowPlacement> Placements { get; set; } = new();
        public string? LastLayout { get; set; }
        public bool AutostartInitialized { get; set; }
        public string Hotkey { get; set; } = "Win+J";

        public static string CurrentLayout => string.Join(";", Screen.AllScreens
            .Select(s => s.Bounds)
            .OrderBy(b => b.X).ThenBy(b => b.Y)
            .Select(b => $"{b.X},{b.Y},{b.Width}x{b.Height}"));

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings();
                }
            }
            catch
            {
                // corrupted settings file - fall back to defaults
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                var tempPath = _settingsPath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tempPath, _settingsPath, overwrite: true);
            }
            catch
            {
                // saving the position is best effort
            }
        }

        /// <summary>
        /// Returns the placement saved for the current monitor layout. Without an exact match,
        /// optionally falls back to the most recently used placement if it is still on screen.
        /// </summary>
        public WindowPlacement? GetPlacement(bool allowFallback)
        {
            if (Placements.TryGetValue(CurrentLayout, out var placement) && placement.IsVisibleOnAnyScreen())
                return placement;

            if (allowFallback && LastLayout is not null
                && Placements.TryGetValue(LastLayout, out var last) && last.IsVisibleOnAnyScreen())
                return last;

            return null;
        }

        public void SetPlacement(Rectangle bounds, bool maximized)
        {
            var layout = CurrentLayout;
            Placements[layout] = new WindowPlacement
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Maximized = maximized,
            };
            LastLayout = layout;
        }
    }
}
