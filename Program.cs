using System.Runtime.InteropServices;

namespace QingShot;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) return SelfTest.Run();

        using var instance = new Mutex(true, @"Local\QingShot.Application", out bool first);
        if (!first)
        {
            MessageBox.Show("轻截图已在运行。按 Alt+A，或双击系统托盘中的绿色截图图标。", "轻截图");
            return 0;
        }

        using var context = new ScreenshotApp(args.Contains("--capture"));
        Application.Run(context);
        return 0;
    }
}

internal sealed class ScreenshotApp : ApplicationContext
{
    private readonly HotkeyWindow hotkey = new();
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly Icon icon;
    private readonly ToolStripMenuItem status;
    private CaptureForm? capture;

    public ScreenshotApp(bool startCapture)
    {
        icon = CreateIcon();
        status = new ToolStripMenuItem { Enabled = false };
        menu.Items.Add("立即截图    Alt+A", null, (_, _) => Capture());
        menu.Items.Add(status);
        menu.Items.Add("重新注册 Alt+A", null, (_, _) => RegisterHotkey());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("使用说明", null, (_, _) => MessageBox.Show(
            "Alt+A：开始截图\n拖动鼠标：框选区域\n选择工具：移动选区；拖动边框：调整大小\n\n" +
            "Enter / Ctrl+C / 双击选区：复制并完成\nCtrl+S：保存 PNG\nCtrl+Z：撤销标注\nEsc / 鼠标右键：取消截图\n\n" +
            "文字输入时 Enter 确认文字，Shift+Enter 换行。\n\n" +
            "若 Alt+A 被微信等程序占用，请先修改对方快捷键，\n再从托盘菜单选择“重新注册 Alt+A”。\n\n截图仅在本机处理。",
            "轻截图 · 使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add("退出轻截图", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = icon, Text = "轻截图 · Alt+A", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Capture();
        hotkey.Pressed += Capture;
        RegisterHotkey();

        if (startCapture)
        {
            var startup = new System.Windows.Forms.Timer { Interval = 200 };
            startup.Tick += (_, _) => { startup.Stop(); startup.Dispose(); Capture(); };
            startup.Start();
        }
    }

    private void RegisterHotkey()
    {
        bool registered = hotkey.Register();
        status.Text = registered ? "Alt+A 已启用" : "Alt+A 被其他程序占用";
        tray.ShowBalloonTip(4000, "轻截图", registered
            ? "已就绪，按 Alt+A 开始截图。双击托盘图标也可截图。"
            : "Alt+A 注册失败，可能已被微信等程序占用。请释放快捷键后，从托盘菜单重新注册；也可双击图标截图。",
            registered ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private void Capture()
    {
        if (capture is not null) { capture.Activate(); return; }
        Bitmap? desktop = null;
        try
        {
            var bounds = SystemInformation.VirtualScreen;
            desktop = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(desktop))
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            capture = new CaptureForm(desktop, bounds);
            desktop = null; // The form owns the frozen desktop from this point.
            capture.FormClosed += (_, _) => capture = null;
            capture.Show();
            capture.Activate();
        }
        catch (Exception ex)
        {
            desktop?.Dispose();
            capture?.Dispose();
            capture = null;
            MessageBox.Show("无法开始截图：" + ex.Message, "轻截图", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(7, 173, 99));
        using var pen = new Pen(Color.White, 2.5f);
        g.DrawRectangle(pen, 8, 8, 16, 16);
        g.DrawLine(pen, 4, 12, 12, 12);
        g.DrawLine(pen, 12, 4, 12, 12);
        g.DrawLine(pen, 20, 20, 28, 20);
        g.DrawLine(pen, 20, 20, 20, 28);
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { Native.DestroyIcon(handle); }
    }

    protected override void ExitThreadCore()
    {
        capture?.Close();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hotkey.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            icon.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private bool registered;
    public event Action? Pressed;

    public HotkeyWindow() => CreateHandle(new CreateParams { Caption = "QingShot.Hotkey", Parent = new IntPtr(-3) });

    public bool Register()
    {
        if (registered) Native.UnregisterHotKey(Handle, 1);
        // MOD_ALT | MOD_NOREPEAT: one capture per key press, even when A is held.
        return registered = Native.RegisterHotKey(Handle, 1, 0x4001, (uint)Keys.A);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam == 1) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (registered) Native.UnregisterHotKey(Handle, 1);
        registered = false;
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}

internal static class Native
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr icon);
}
