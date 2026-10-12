using Microsoft.Win32;

namespace QingShot;

internal static class Startup
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QingShot";
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled) key.SetValue(ValueName, Command(Environment.ProcessPath
            ?? throw new IOException("无法确定程序路径。")), RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }

    internal static string Command(string path) => $"\"{Path.GetFullPath(path)}\"";
}

internal sealed class ShortcutGate
{
    private bool held;
    public void Reset() => held = false;
    public (bool Suppress, bool Trigger) Process(uint key, bool down, bool alt, bool ctrl, bool shift, bool win)
    {
        if (key != (uint)Keys.A) return (false, false);
        if (held)
        {
            if (!down) held = false;
            return (true, false);
        }
        if (!down || !alt || ctrl || shift || win) return (false, false);
        held = true;
        return (true, true);
    }
}
