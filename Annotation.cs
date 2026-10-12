using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace QingShot;

internal enum DrawTool { Select, Rectangle, Ellipse, Arrow, Pen, Text, Mosaic }

internal sealed class Annotation(DrawTool tool, Color color, float width, Point start)
{
    public DrawTool Tool { get; } = tool;
    public Color Color { get; } = color;
    public float Width { get; } = width;
    public List<Point> Points { get; } = [start];
    public string Text { get; set; } = "";
    public Point End { get; set; } = start;
    public Rectangle Bounds => Selection.Between(Points[0], End);

    public void Draw(Graphics graphics, Bitmap desktop)
    {
        using var pen = new Pen(Color, Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var start = Points[0];
        var rect = Bounds;
        switch (Tool)
        {
            case DrawTool.Rectangle:
                if (rect.Width > 0 && rect.Height > 0) graphics.DrawRectangle(pen, rect);
                break;
            case DrawTool.Ellipse:
                if (rect.Width > 0 && rect.Height > 0) graphics.DrawEllipse(pen, rect);
                break;
            case DrawTool.Arrow:
                if (start == End) break;
                using (var arrow = new AdjustableArrowCap(4, 5, true))
                {
                    pen.CustomEndCap = arrow;
                    graphics.DrawLine(pen, start, End);
                }
                break;
            case DrawTool.Pen:
                if (Points.Count > 1) graphics.DrawLines(pen, Points.ToArray());
                else
                {
                    using var dot = new SolidBrush(Color);
                    graphics.FillEllipse(dot, start.X - Width / 2, start.Y - Width / 2, Width, Width);
                }
                break;
            case DrawTool.Text:
                using (var font = new Font("Microsoft YaHei UI", 16 + Width * 2, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Color))
                using (var format = TextFormat())
                    graphics.DrawString(Text, font, brush, start, format);
                break;
            case DrawTool.Mosaic:
                DrawMosaic(graphics, desktop, rect, Math.Max(8, (int)Width * 4));
                break;
        }
    }

    internal static StringFormat TextFormat()
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        return format;
    }

    private static void DrawMosaic(Graphics g, Bitmap desktop, Rectangle rect, int block)
    {
        rect.Intersect(new Rectangle(Point.Empty, desktop.Size));
        if (rect.Width <= 0 || rect.Height <= 0) return;
        using var small = new Bitmap(Math.Max(1, rect.Width / block), Math.Max(1, rect.Height / block));
        using (var tiny = Graphics.FromImage(small))
        {
            tiny.InterpolationMode = InterpolationMode.HighQualityBilinear;
            tiny.DrawImage(desktop, new Rectangle(Point.Empty, small.Size), rect, GraphicsUnit.Pixel);
        }
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(small, rect, 0, 0, small.Width, small.Height, GraphicsUnit.Pixel);
        g.Restore(state);
    }
}

internal static class Selection
{
    public static Rectangle Between(Point a, Point b) => Rectangle.FromLTRB(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public static Rectangle Move(Rectangle rect, Point delta, Size area) => new(
        Math.Clamp(rect.X + delta.X, 0, area.Width - rect.Width),
        Math.Clamp(rect.Y + delta.Y, 0, area.Height - rect.Height), rect.Width, rect.Height);

    public static Bitmap Render(Bitmap desktop, Rectangle selection, IEnumerable<Annotation> annotations)
    {
        selection.Intersect(new Rectangle(Point.Empty, desktop.Size));
        if (selection.Width < 1 || selection.Height < 1) throw new ArgumentException("请先框选截图区域。");
        var result = new Bitmap(selection.Width, selection.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImage(desktop, new Rectangle(Point.Empty, result.Size), selection, GraphicsUnit.Pixel);
        graphics.TranslateTransform(-selection.X, -selection.Y);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.SetClip(selection);
        foreach (var annotation in annotations) annotation.Draw(graphics, desktop);
        return result;
    }
}
