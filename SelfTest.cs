using System.Reflection;

namespace QingShot;

internal static class SelfTest
{
    public static int Run()
    {
        string report = Path.Combine(AppContext.BaseDirectory, "self-test.txt");
        try
        {
            Check(Selection.Between(new Point(90, 70), new Point(10, 20)) == new Rectangle(10, 20, 80, 50), "reverse selection");
            Check(Selection.Move(new Rectangle(10, 20, 80, 50), new Point(-100, 100), new Size(120, 100)) == new Rectangle(0, 50, 80, 50), "selection stays on desktop");
            using var source = new Bitmap(120, 100);
            for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                    source.SetPixel(x, y, Color.FromArgb(x * 2, y * 2, (x + y) % 256));

            var crop = new Rectangle(25, 20, 60, 50);
            using (var result = Selection.Render(source, crop, []))
            {
                Check(result.Size == crop.Size, "exact crop dimensions");
                Check(result.GetPixel(0, 0) == source.GetPixel(25, 20), "top-left pixel alignment");
                Check(result.GetPixel(59, 49) == source.GetPixel(84, 69), "bottom-right pixel alignment");
            }
            using (var clipped = Selection.Render(source, new Rectangle(-10, -10, 20, 20), []))
                Check(clipped.Size == new Size(10, 10), "out-of-bounds crop clipping");

            var arrow = new Annotation(DrawTool.Arrow, Color.Red, 3, new Point(30, 30)) { End = new Point(70, 50) };
            using (var result = Selection.Render(source, crop, [arrow]))
                Check(result.GetPixel(25, 20).R > 200, "arrow is translated into crop coordinates");

            var mosaic = new Annotation(DrawTool.Mosaic, Color.Red, 3, new Point(25, 20)) { End = new Point(80, 65) };
            using (var result = Selection.Render(source, crop, [mosaic]))
            {
                Check(result.GetPixel(3, 3) == result.GetPixel(4, 4), "mosaic pixels form blocks");
                Check(result.GetPixel(59, 49) == source.GetPixel(84, 69), "mosaic stays inside its region");
            }
            var marks = new List<Annotation>
            {
                new(DrawTool.Rectangle, Color.Red, 3, new Point(30, 25)) { End = new Point(65, 60) },
                new(DrawTool.Ellipse, Color.Lime, 2, new Point(35, 30)) { End = new Point(70, 65) },
                new(DrawTool.Pen, Color.Blue, 3, new Point(40, 40)),
                new(DrawTool.Text, Color.White, 2, new Point(28, 22)) { Text = "截图\nTest" }
            };
            using (var combined = Selection.Render(source, crop, marks))
                Check(combined.Size == crop.Size, "all annotation tools render together");

            string png = Path.Combine(Path.GetTempPath(), $"qingshot-test-{Guid.NewGuid():N}.png");
            try
            {
                CaptureForm.SavePng(source, png);
                using (var saved = new Bitmap(png))
                {
                    Check(saved.Size == source.Size, "PNG save round-trip dimensions");
                    Check(saved.GetPixel(80, 60) == source.GetPixel(80, 60), "PNG save is lossless");
                }
                using var replacement = new Bitmap(32, 24);
                CaptureForm.SavePng(replacement, png);
                using var replaced = new Bitmap(png);
                Check(replaced.Size == replacement.Size, "overwrite existing PNG");
            }
            finally { if (File.Exists(png)) File.Delete(png); }

            using (var form = new CaptureForm((Bitmap)source.Clone(), new Rectangle(-120, 0, 120, 100)))
                Check(form.Bounds.Location == new Point(-120, 0), "negative virtual-screen origin");

            CheckInteraction();
            string hotkeyResult;
            using (var first = new HotkeyWindow())
            using (var second = new HotkeyWindow())
            {
                if (first.Register())
                {
                    Check(!second.Register(), "hotkey conflict is reported");
                    Check(first.Register(), "hotkey can be re-registered");
                    first.Dispose();
                    Check(second.Register(), "hotkey is released on disposal");
                    hotkeyResult = "PASS: native Alt+A registration, conflict, retry and release.";
                }
                else hotkeyResult = "SKIP: native Alt+A already occupied; conflict is reported correctly.";
            }

            File.WriteAllText(report, "PASS: selection, boundary clamping, pixel-exact crop, annotation translation, mosaic, all drawing tools, PNG save/overwrite, negative screen origin.\n" +
                "PASS: interaction handlers for reverse drag, move, resize, annotation, undo, text commit/cancel, reselect; toolbar and export previews generated.\n" + hotkeyResult + "\n");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(report, "FAIL: " + ex + "\n");
            return 1;
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }

    private static T Read<T>(CaptureForm form, string name) => (T)typeof(CaptureForm)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    // Exercise the same click handler without requiring a visible desktop window.
    private static void ClickButton(Button button) => typeof(Control)
        .GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, [EventArgs.Empty]);

    private static void CheckInteraction()
    {
        using var sample = DemoDesktop();
        using var form = new InteractionForm((Bitmap)sample.Clone());
        form.CreateControl();
        form.Drag(new Point(980, 610), new Point(220, 130));
        Check(Read<Rectangle>(form, "selection") == new Rectangle(220, 130, 760, 480), "reverse drag interaction");
        form.Drag(new Point(500, 400), new Point(520, 420));
        Check(Read<Rectangle>(form, "selection") == new Rectangle(240, 150, 760, 480), "move selection interaction");
        form.Drag(new Point(1000, 630), new Point(1040, 650));
        Check(Read<Rectangle>(form, "selection") == new Rectangle(240, 150, 800, 500), "resize selection interaction");

        var buttons = Read<Dictionary<DrawTool, Button>>(form, "toolButtons");
        var marks = Read<List<Annotation>>(form, "annotations");
        var widths = Read<Dictionary<float, Button>>(form, "widthButtons");
        ClickButton(widths[6]);
        Check(Read<float>(form, "lineWidth") == 6, "thick stroke option");
        ClickButton(widths[3]);
        foreach (var drawing in new[] { DrawTool.Rectangle, DrawTool.Ellipse, DrawTool.Arrow, DrawTool.Pen, DrawTool.Mosaic })
        {
            ClickButton(buttons[drawing]);
            form.Drag(new Point(270, 230), new Point(600, 390));
            Check(marks.Count == 1 && marks[0].Tool == drawing, $"{drawing} drag creates a mark");
            form.Key(Keys.Control | Keys.Z);
            Check(marks.Count == 0, $"{drawing} undo interaction");
        }

        ClickButton(buttons[DrawTool.Text]);
        form.ClickAt(new Point(290, 210));
        Read<TextBox>(form, "editor").Text = "轻截图 · Alt+A";
        form.Key(Keys.Enter);
        Check(marks.Count == 1 && marks[0].Text == "轻截图 · Alt+A", "text commit interaction");
        form.ClickAt(new Point(400, 260));
        Read<TextBox>(form, "editor").Text = "discard";
        form.Key(Keys.Escape);
        Check(marks.Count == 1 && !form.IsDisposed, "Escape in text editor preserves screenshot");
        ClickButton(buttons[DrawTool.Rectangle]);
        form.Drag(new Point(270, 190), new Point(680, 280));
        ClickButton(buttons[DrawTool.Arrow]);
        form.Drag(new Point(735, 320), new Point(650, 265));
        ClickButton(buttons[DrawTool.Mosaic]);
        form.Drag(new Point(290, 450), new Point(635, 505));
        ClickButton(buttons[DrawTool.Select]);

        string artifacts = Path.Combine(AppContext.BaseDirectory, "verification");
        Directory.CreateDirectory(artifacts);
        using (var preview = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(preview, form.ClientRectangle);
            // A hidden form does not print child windows; render its toolbar separately.
            var toolbar = Read<Panel>(form, "toolbar");
            using var toolbarImage = RenderControl(toolbar);
            using (var graphics = Graphics.FromImage(preview))
                graphics.DrawImageUnscaled(toolbarImage, toolbar.Location);
            toolbarImage.Save(Path.Combine(artifacts, "toolbar-preview.png"));
            preview.Save(Path.Combine(artifacts, "editor-preview.png"));
        }
        using (var cropped = Selection.Render(sample, Read<Rectangle>(form, "selection"), marks))
        {
            cropped.Save(Path.Combine(artifacts, "export-preview.png"));
            Check(cropped.Size == new Size(800, 500), "export excludes toolbar and shade");
            var clipboardData = new DataObject(cropped);
            Check(clipboardData.GetDataPresent(DataFormats.Bitmap), "clipboard payload contains a bitmap");
        }
        int previousCount = marks.Count;
        form.ClickAt(new Point(50, 40));
        Check(marks.Count == previousCount && Read<Rectangle>(form, "selection").Size == new Size(800, 500),
            "accidental click outside selection preserves work");
        form.Drag(new Point(50, 40), new Point(500, 300));
        Check(marks.Count == 0, "new selection resets annotations");
        form.Key(Keys.Escape);
        Check(form.IsDisposed, "Escape closes screenshot");
    }

    private static Bitmap RenderControl(Control control)
    {
        control.PerformLayout();
        var image = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(image, control.ClientRectangle);
        using var graphics = Graphics.FromImage(image);
        foreach (Control child in control.Controls)
        {
            using var childImage = RenderControl(child);
            graphics.DrawImageUnscaled(childImage, child.Location);
        }
        return image;
    }

    private static Bitmap DemoDesktop()
    {
        var image = new Bitmap(1280, 800);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.FromArgb(231, 237, 242));
        using var header = new SolidBrush(Color.FromArgb(24, 37, 49));
        g.FillRectangle(header, 0, 0, 1280, 88);
        using var titleFont = new Font("Microsoft YaHei UI", 28, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Microsoft YaHei UI", 20, FontStyle.Regular, GraphicsUnit.Pixel);
        g.DrawString("项目工作台", titleFont, Brushes.White, 64, 26);
        g.FillRectangle(Brushes.White, 180, 115, 900, 570);
        g.DrawString("本周计划", titleFont, Brushes.Black, 270, 185);
        g.DrawString("产品设计    /    截图工具体验验证", bodyFont, Brushes.SlateGray, 270, 285);
        g.DrawString("01   框选区域并标注关键信息", bodyFont, Brushes.SlateGray, 270, 350);
        g.DrawString("02   私密区域使用马赛克遮挡", bodyFont, Brushes.SlateGray, 290, 455);
        g.DrawString("03   Enter 复制，Ctrl+S 保存", bodyFont, Brushes.SlateGray, 270, 550);
        return image;
    }

    private sealed class InteractionForm(Bitmap image) : CaptureForm(image, new Rectangle(Point.Empty, image.Size))
    {
        public void Drag(Point from, Point to)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, from.X, from.Y, 0));
            OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, to.X, to.Y, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, to.X, to.Y, 0));
        }

        public void ClickAt(Point at)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));
        }

        public void Key(Keys key)
        {
            var message = new Message();
            ProcessCmdKey(ref message, key);
        }
    }
}
