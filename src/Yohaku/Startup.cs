using Microsoft.Win32;

namespace Yohaku;

/// <summary>
/// Manages Yohaku's "start at login" entry under the per-user Run key. No admin
/// rights needed; the entry launches the current executable at sign-in.
/// </summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Yohaku";

    // Quoted so a path containing spaces is launched as a single argument.
    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception ex) { Log.Warn($"Could not read start-at-login state: {ex.Message}"); return false; }
    }

    public static void Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(ValueName, Command);
        }
        catch (Exception ex) { Log.Warn($"Could not enable start-at-login: {ex.Message}"); }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) { Log.Warn($"Could not disable start-at-login: {ex.Message}"); }
    }

    /// <summary>
    /// If enabled but the stored command points elsewhere (the executable was moved),
    /// refresh it to the current path. Call once at startup.
    /// </summary>
    public static void SyncPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string stored && stored != Command)
                key.SetValue(ValueName, Command);
        }
        catch (Exception ex) { Log.Warn($"Could not sync start-at-login path: {ex.Message}"); }
    }
}
