using System.Runtime.InteropServices;
using System.Text;

namespace QingShot;

internal static class WindowDetection
{
    // Snapshot front-to-back order before the capture overlay is shown.
    public static IReadOnlyList<Rectangle> Snapshot(Rectangle desktop)
    {
        var windows = new List<Rectangle>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || IsIconic(window)) return true;
            if (DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if (DwmGetWindowAttribute(window, 9, out WindowRect bounds, Marshal.SizeOf<WindowRect>()) != 0 &&
                !GetWindowRect(window, out bounds)) return true;
            var rect = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            rect.Intersect(desktop);
            if (rect.Width < 2 || rect.Height < 2) return true;
            rect.Offset(-desktop.X, -desktop.Y);
            windows.Add(rect);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public static Rectangle At(IReadOnlyList<Rectangle> windows, Point point)
    {
        foreach (var window in windows) if (window.Contains(point)) return window;
        return Rectangle.Empty;
    }

    private delegate bool EnumCallback(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out WindowRect value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
