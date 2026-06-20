namespace Yohaku;

/// <summary>Minimal logging to a size-capped file plus Debug output.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static string LogPath => Path.Combine(Config.ConfigDir, "yohaku.log");

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Config.ConfigDir);
                // Truncate if the log gets large (>1 MB).
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
                    File.WriteAllText(LogPath, string.Empty);
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
