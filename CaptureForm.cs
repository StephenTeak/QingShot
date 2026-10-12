using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace QingShot;

internal class CaptureForm : Form
{
    private static readonly Color Accent = Color.FromArgb(7, 193, 112);
    private readonly Bitmap desktop;
    private readonly Rectangle screenBounds;
    private readonly IReadOnlyList<Rectangle> windows;
    private Rectangle hoveredWindow;
    private readonly List<Annotation> annotations = [];
    private readonly Dictionary<DrawTool, Button> toolButtons = [];
    private readonly Dictionary<float, Button> widthButtons = [];
    private readonly ToolTip tips = new();
    private readonly Panel toolbar = new();
    private readonly List<Button> swatches = [];
    private readonly Font uiFont = new("Microsoft YaHei UI", 9);
    private Rectangle selection;
    private Rectangle beforeDrag;
    private Point dragStart;
    private Annotation? pending;
    private DrawTool tool;
    private Color ink = Color.FromArgb(255, 81, 81);
    private float lineWidth = 3;
    private int resizeEdges;
    private bool selecting, moving, dragging, finishing;
    private TextBox? editor;
    private Point textAnchor;
    private Button undoButton = null!;

    public CaptureForm(Bitmap desktop, Rectangle bounds, IReadOnlyList<Rectangle>? windows = null)
    {
        this.desktop = desktop;
        screenBounds = bounds;
        this.windows = windows ?? [];
        hoveredWindow = WindowDetection.At(this.windows, new Point(MousePosition.X - bounds.X, MousePosition.Y - bounds.Y));
        Text = "轻截图 · 框选屏幕";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = bounds;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        Font = uiFont;
        BuildToolbar();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = screenBounds;
        Activate();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        e.Cancel = true; // The desktop bitmap and selection use physical virtual-screen pixels.
        base.OnDpiChanged(e);
        Bounds = screenBounds;
    }

    private void BuildToolbar()
    {
        float scale = Math.Min(DeviceDpi / 96f, Math.Max(1, Screen.FromPoint(MousePosition).Bounds.Width / 790f));
        int Px(int n) => (int)Math.Round(n * scale);
        toolbar.BackColor = Color.FromArgb(36, 39, 44);
        toolbar.Size = new Size(Px(762), Px(92));
        toolbar.Visible = false;
        toolbar.Padding = new Padding(Px(8));
        toolbar.AccessibleName = "截图标注工具栏";
        Controls.Add(toolbar);
        var row = new FlowLayoutPanel
        {
            Location = new Point(Px(8), Px(8)), Size = new Size(Px(746), Px(36)),
            WrapContents = false, BackColor = Color.Transparent
        };
        toolbar.Controls.Add(row);

        Button Add(string label, string description, Action action, int width = 59)
        {
            var button = new Button
            {
                Text = label, AccessibleName = description, Size = new Size(Px(width), Px(34)),
                Margin = new Padding(0, 0, Px(3), 0), FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White, BackColor = toolbar.BackColor, Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false, TabStop = true
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(68, 73, 81);
            button.Click += (_, _) => action();
            tips.SetToolTip(button, description);
            row.Controls.Add(button);
            return button;
        }
        foreach (var (value, label) in new[]
        {
            (DrawTool.Select, "选择"), (DrawTool.Rectangle, "矩形"), (DrawTool.Ellipse, "椭圆"),
            (DrawTool.Arrow, "箭头"), (DrawTool.Pen, "画笔"), (DrawTool.Text, "文字"), (DrawTool.Mosaic, "马赛克")
        })
        {
            var current = value;
            toolButtons[value] = Add(label, value == DrawTool.Select ? "选择：拖动选区移动，拖动边框调整大小" : label,
                () => { CommitText(); tool = current; UpdateTools(); Focus(); });
        }
        undoButton = Add("撤销", "撤销上一次标注 (Ctrl+Z)", Undo);
        Add("保存", "另存为 PNG (Ctrl+S)", Save);
        Add("取消", "取消截图 (Esc)", Close);
        var finish = Add("✓ 完成", "复制到剪贴板 (Enter / Ctrl+C)", Copy, 116);
        finish.BackColor = Accent;
        finish.ForeColor = Color.FromArgb(10, 30, 20);

        var options = new FlowLayoutPanel
        {
            Location = new Point(Px(12), Px(53)), Size = new Size(Px(738), Px(30)),
            WrapContents = false, BackColor = Color.Transparent
        };
        toolbar.Controls.Add(options);
        foreach (var color in new[] { ink, Color.FromArgb(255, 185, 50), Color.FromArgb(252, 230, 66), Accent,
            Color.FromArgb(60, 163, 255), Color.FromArgb(182, 128, 255), Color.White, Color.Black })
        {
            var swatch = new Button
            {
                Size = new Size(Px(22), Px(22)), Margin = new Padding(0, Px(2), Px(8), 0),
                BackColor = color, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                AccessibleName = $"标注颜色 {color.Name}", Tag = color
            };
            swatch.Click += (_, _) => { CommitText(); ink = color; UpdateTools(); Focus(); };
            swatches.Add(swatch);
            options.Controls.Add(swatch);
        }
        options.Controls.Add(new Label { Text = "粗细", ForeColor = Color.Silver, AutoSize = true, Margin = new Padding(Px(10), Px(5), Px(5), 0) });
        foreach (var (width, label) in new[] { (2f, "细"), (3f, "中"), (6f, "粗") })
        {
            var button = new Button
            {
                Text = label, AccessibleName = "画笔粗细：" + label, FlatStyle = FlatStyle.Flat,
                Size = new Size(Px(30), Px(26)), Margin = new Padding(0, 0, Px(3), 0),
                ForeColor = Color.White, Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => { CommitText(); lineWidth = width; UpdateTools(); Focus(); };
            widthButtons[width] = button;
            options.Controls.Add(button);
        }
        options.Controls.Add(new Label
        {
            Text = "Enter 完成   ·   Ctrl+Z 撤销   ·   Esc 取消", ForeColor = Color.FromArgb(174, 181, 192),
            AutoSize = true, Margin = new Padding(Px(24), Px(5), 0, 0)
        });
        UpdateTools();
    }

    private void UpdateTools()
    {
        foreach (var (value, button) in toolButtons)
        {
            button.BackColor = value == tool ? Color.FromArgb(41, 78, 63) : toolbar.BackColor;
            button.ForeColor = value == tool ? Accent : Color.White;
        }
        foreach (var swatch in swatches)
        {
            swatch.FlatAppearance.BorderColor = (Color)swatch.Tag! == ink ? Color.White : Color.FromArgb(96, 100, 107);
            swatch.FlatAppearance.BorderSize = (Color)swatch.Tag! == ink ? 3 : 1;
        }
        foreach (var (width, button) in widthButtons)
        {
            button.BackColor = width == lineWidth ? Color.FromArgb(41, 78, 63) : toolbar.BackColor;
            button.ForeColor = width == lineWidth ? Accent : Color.Silver;
        }
        undoButton.Enabled = annotations.Count > 0 || editor is not null;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var highlighted = selection.IsEmpty && !dragging ? hoveredWindow : selection;
        g.DrawImageUnscaled(desktop, Point.Empty);
        using (var shade = new SolidBrush(Color.FromArgb(125, 0, 0, 0)))
        using (var outside = new Region(ClientRectangle))
        {
            if (!highlighted.IsEmpty) outside.Exclude(highlighted);
            g.FillRegion(shade, outside);
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (selection.Width > 0 && selection.Height > 0)
        {
            var state = g.Save();
            g.SetClip(selection);
            if (!selecting || !dragging)
                foreach (var annotation in annotations) annotation.Draw(g, desktop);
            pending?.Draw(g, desktop);
            g.Restore(state);
            using var border = new Pen(Accent, 1);
            g.DrawRectangle(border, selection.X, selection.Y, selection.Width, selection.Height);
            if (!dragging)
            {
                foreach (var (point, _) in Handles())
                {
                    g.FillRectangle(Brushes.White, point.X - 3, point.Y - 3, 6, 6);
                    g.DrawRectangle(border, point.X - 3, point.Y - 3, 6, 6);
                }
            }
            DrawBadge(g, $"{selection.Width} × {selection.Height} px", new Point(selection.X, selection.Y - 32));
        }
        else if (highlighted.IsEmpty)
        {
            var monitor = Screen.FromPoint(MousePosition).Bounds;
            monitor.Offset(-screenBounds.X, -screenBounds.Y);
            const string hint = "悬停识别窗口 · 单击选中 · 拖动框选 · Esc / 右键取消";
            var size = TextRenderer.MeasureText(hint, Font);
            DrawBadge(g, hint, new Point(monitor.Left + (monitor.Width - size.Width) / 2, monitor.Top + monitor.Height / 2));
        }
        base.OnPaint(e);
        if (selection.IsEmpty && !dragging && !hoveredWindow.IsEmpty)
        {
            using var border = new Pen(Accent, 2);
            g.DrawRectangle(border, hoveredWindow);
            DrawBadge(g, $"窗口 {hoveredWindow.Width} × {hoveredWindow.Height} px · 单击选中 / 拖动框选",
                new Point(hoveredWindow.X, hoveredWindow.Y - 32));
        }
    }

    private void DrawBadge(Graphics g, string text, Point at)
    {
        var size = TextRenderer.MeasureText(text, Font);
        var box = new Rectangle(Math.Clamp(at.X, 0, Math.Max(0, Width - size.Width - 20)),
            Math.Clamp(at.Y, 0, Math.Max(0, Height - size.Height - 12)), size.Width + 20, size.Height + 12);
        using var brush = new SolidBrush(Color.FromArgb(235, 31, 34, 39));
        g.FillRectangle(brush, box);
        TextRenderer.DrawText(g, text, Font, new Point(box.X + 10, box.Y + 6), Color.White, TextFormatFlags.NoPadding);
    }

    private IEnumerable<(Point Point, int Edges)> Handles()
    {
        var r = selection;
        int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
        return [(new(r.Left, r.Top), 3), (new(cx, r.Top), 2), (new(r.Right, r.Top), 6),
            (new(r.Right, cy), 4), (new(r.Right, r.Bottom), 12), (new(cx, r.Bottom), 8),
            (new(r.Left, r.Bottom), 9), (new(r.Left, cy), 1)];
    }

    private int HitEdges(Point p)
    {
        if (selection.IsEmpty) return 0;
        foreach (var (handle, edges) in Handles())
            if (Math.Abs(handle.X - p.X) <= 6 && Math.Abs(handle.Y - p.Y) <= 6) return edges;
        var expanded = Rectangle.Inflate(selection, 4, 4);
        if (!expanded.Contains(p)) return 0;
        if (Math.Abs(p.X - selection.Left) <= 4) return 1;
        if (Math.Abs(p.X - selection.Right) <= 4) return 4;
        if (Math.Abs(p.Y - selection.Top) <= 4) return 2;
        if (Math.Abs(p.Y - selection.Bottom) <= 4) return 8;
        return 0;
    }

    private Point ClampToDesktop(Point p) => new(Math.Clamp(p.X, 0, desktop.Width), Math.Clamp(p.Y, 0, desktop.Height));
    private Point ClampToSelection(Point p) => new(Math.Clamp(p.X, selection.Left, selection.Right - 1), Math.Clamp(p.Y, selection.Top, selection.Bottom - 1));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right) { Close(); return; }
        if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
        CommitText();
        Focus();
        dragStart = ClampToDesktop(e.Location);
        beforeDrag = selection;
        resizeEdges = HitEdges(e.Location);
        selecting = selection.IsEmpty || (resizeEdges == 0 && !selection.Contains(e.Location));
        moving = !selecting && resizeEdges == 0 && tool == DrawTool.Select;
        if (selecting)
        {
            selection = Rectangle.Empty;
            tool = DrawTool.Select;
            UpdateTools();
        }
        else if (resizeEdges == 0 && !moving)
        {
            if (tool == DrawTool.Text) { BeginText(ClampToSelection(e.Location)); return; }
            pending = new Annotation(tool, ink, lineWidth, ClampToSelection(e.Location));
        }
        dragging = true;
        Capture = true;
        toolbar.Hide();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging)
        {
            if (selection.IsEmpty)
            {
                var candidate = WindowDetection.At(windows, e.Location);
                if (candidate != hoveredWindow) { hoveredWindow = candidate; Invalidate(); }
            }
            Cursor = HitEdges(e.Location) switch
            {
                3 or 12 => Cursors.SizeNWSE, 6 or 9 => Cursors.SizeNESW,
                1 or 4 => Cursors.SizeWE, 2 or 8 => Cursors.SizeNS,
                _ => selection.Contains(e.Location) && tool == DrawTool.Select ? Cursors.SizeAll :
                    selection.Contains(e.Location) && tool == DrawTool.Text ? Cursors.IBeam : Cursors.Cross
            };
            return;
        }
        var p = ClampToDesktop(e.Location);
        if (selecting) selection = Selection.Between(dragStart, p);
        else if (moving)
            selection = Selection.Move(beforeDrag, new Point(p.X - dragStart.X, p.Y - dragStart.Y), desktop.Size);
        else if (resizeEdges != 0)
        {
            int l = (resizeEdges & 1) != 0 ? p.X : beforeDrag.Left;
            int t = (resizeEdges & 2) != 0 ? p.Y : beforeDrag.Top;
            int r = (resizeEdges & 4) != 0 ? p.X : beforeDrag.Right;
            int b = (resizeEdges & 8) != 0 ? p.Y : beforeDrag.Bottom;
            selection = Selection.Between(new Point(l, t), new Point(r, b));
        }
        else if (pending is not null)
        {
            pending.End = ClampToSelection(p);
            if (pending.Tool == DrawTool.Pen) pending.Points.Add(pending.End);
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !dragging) return;
        OnMouseMove(e); // Preserve the release point even when the last move was coalesced.
        dragging = false;
        Capture = false;
        if (selecting && beforeDrag.IsEmpty && Math.Abs(e.X - dragStart.X) < SystemInformation.DragSize.Width / 2 &&
            Math.Abs(e.Y - dragStart.Y) < SystemInformation.DragSize.Height / 2)
            selection = WindowDetection.At(windows, dragStart);
        if (selection.Width < 2 || selection.Height < 2) selection = beforeDrag;
        else if (selecting) annotations.Clear();
        if (pending is not null && (pending.Tool == DrawTool.Pen || pending.End != pending.Points[0])) annotations.Add(pending);
        pending = null;
        PositionToolbar();
        UpdateTools();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && tool == DrawTool.Select && selection.Contains(e.Location)) Copy();
    }

    private void PositionToolbar()
    {
        if (selection.IsEmpty || dragging) { toolbar.Hide(); return; }
        var monitor = Screen.FromPoint(PointToScreen(new Point(selection.Right - 1, selection.Bottom - 1))).Bounds;
        monitor.Offset(-screenBounds.X, -screenBounds.Y);
        monitor.Intersect(ClientRectangle);
        int x = Math.Clamp(selection.Right - toolbar.Width, monitor.Left + 8, Math.Max(monitor.Left + 8, monitor.Right - toolbar.Width - 8));
        int y = selection.Bottom + 12;
        if (y + toolbar.Height > monitor.Bottom - 8) y = selection.Top - toolbar.Height - 12;
        if (y < monitor.Top + 8) y = monitor.Bottom - toolbar.Height - 8;
        toolbar.Location = new Point(x, y);
        toolbar.Show();
        toolbar.BringToFront();
    }

    private void BeginText(Point at)
    {
        textAnchor = at;
        editor = new TextBox
        {
            Multiline = true, AcceptsReturn = true, BorderStyle = BorderStyle.FixedSingle,
            Location = at, Size = new Size(Math.Min(300, selection.Right - at.X), Math.Min(110, selection.Bottom - at.Y)),
            ForeColor = ink, BackColor = Color.FromArgb(30, 32, 36),
            Font = new Font("Microsoft YaHei UI", 16 + lineWidth * 2, FontStyle.Regular, GraphicsUnit.Pixel),
            AccessibleName = "标注文字，Enter 确认，Shift+Enter 换行", MaxLength = 4000
        };
        Controls.Add(editor);
        editor.BringToFront();
        editor.Focus();
        UpdateTools();
    }

    private void CommitText()
    {
        if (editor is null) return;
        if (!string.IsNullOrWhiteSpace(editor.Text))
            annotations.Add(new Annotation(DrawTool.Text, ink, lineWidth, textAnchor) { Text = editor.Text });
        RemoveEditor();
        UpdateTools();
    }

    private void RemoveEditor()
    {
        if (editor is null) return;
        var font = editor.Font;
        Controls.Remove(editor);
        editor.Dispose();
        editor = null;
        font.Dispose();
        if (!Disposing) Focus();
        Invalidate();
    }

    private void Undo()
    {
        if (editor is not null) RemoveEditor();
        else if (annotations.Count > 0) annotations.RemoveAt(annotations.Count - 1);
        UpdateTools();
        Focus();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (editor is not null)
        {
            if (keyData == Keys.Escape) { RemoveEditor(); return true; }
            if (keyData == Keys.Enter) { CommitText(); return true; }
            if (keyData != (Keys.Control | Keys.S)) return base.ProcessCmdKey(ref msg, keyData);
        }
        switch (keyData)
        {
            case Keys.Escape: Close(); return true;
            case Keys.Enter:
            case Keys.Control | Keys.C: Copy(); return true;
            case Keys.Control | Keys.S: Save(); return true;
            case Keys.Control | Keys.Z: Undo(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Copy()
    {
        if (selection.IsEmpty || finishing || dragging) return;
        CommitText();
        finishing = true;
        try
        {
            using var image = Selection.Render(desktop, selection, annotations);
            // WinForms retries if another process briefly holds the clipboard open.
            Clipboard.SetDataObject(image, true, 10, 80);
            Close();
        }
        catch (ExternalException)
        {
            MessageBox.Show(this, "剪贴板正被其他程序占用，请重试，或按 Ctrl+S 保存截图。", "轻截图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { finishing = false; }
    }

    private void Save()
    {
        if (selection.IsEmpty || finishing || dragging) return;
        CommitText();
        finishing = true;
        try
        {
            using var dialog = new SaveFileDialog
            {
                Title = "保存截图", Filter = "PNG 图片 (*.png)|*.png", DefaultExt = "png", AddExtension = true,
                FileName = $"截图_{DateTime.Now:yyyyMMdd_HHmmss}.png", OverwritePrompt = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            using var image = Selection.Render(desktop, selection, annotations);
            // Encode first, then replace on the same volume so failed writes preserve an existing file.
            SavePng(image, dialog.FileName);
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
        {
            MessageBox.Show(this, "保存失败，截图仍保留，可更换位置重试。\n" + ex.Message, "轻截图", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { finishing = false; }
    }

    internal static void SavePng(Bitmap image, string path)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".qingshot-{Guid.NewGuid():N}.tmp");
        try
        {
            image.Save(temporary, ImageFormat.Png);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RemoveEditor();
            desktop.Dispose();
            tips.Dispose();
            uiFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
