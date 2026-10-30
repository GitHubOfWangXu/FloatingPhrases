using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;

namespace FloatingPhrases;

public enum ScreenshotDrawingTool { Rectangle, Line, Arrow, Pen }

public static class ScreenshotDrawing
{
    public static Stroke CreateShape(ScreenshotDrawingTool tool, Point start, Point end, DrawingAttributes attributes)
    {
        var points = new StylusPointCollection();
        if (tool == ScreenshotDrawingTool.Rectangle)
        {
            var left = Math.Min(start.X, end.X);
            var top = Math.Min(start.Y, end.Y);
            var right = Math.Max(start.X, end.X);
            var bottom = Math.Max(start.Y, end.Y);
            foreach (var point in new[] { new Point(left, top), new Point(right, top), new Point(right, bottom), new Point(left, bottom), new Point(left, top) })
                points.Add(new StylusPoint(point.X, point.Y));
        }
        else if (tool is ScreenshotDrawingTool.Line or ScreenshotDrawingTool.Arrow)
        {
            points.Add(new StylusPoint(start.X, start.Y));
            points.Add(new StylusPoint(end.X, end.Y));
            var direction = end - start;
            if (tool == ScreenshotDrawingTool.Arrow && direction.LengthSquared > 0)
            {
                var headLength = Math.Min(direction.Length * 0.45, Math.Max(12, attributes.Width * 4));
                direction.Normalize();
                var back = end - direction * headLength;
                var wing = new Vector(-direction.Y, direction.X) * headLength * 0.5;
                var first = back + wing;
                var second = back - wing;
                points.Add(new StylusPoint(first.X, first.Y));
                points.Add(new StylusPoint(end.X, end.Y));
                points.Add(new StylusPoint(second.X, second.Y));
            }
        }
        else throw new ArgumentOutOfRangeException(nameof(tool));
        var style = attributes.Clone();
        style.FitToCurve = false;
        return new Stroke(points, style);
    }

    public static BitmapSource Render(BitmapSource original, StrokeCollection strokes)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, original.PixelWidth, original.PixelHeight);
            drawing.PushClip(new RectangleGeometry(bounds));
            drawing.DrawImage(original, bounds);
            foreach (var stroke in strokes) stroke.Draw(drawing);
            drawing.Pop();
        }
        // 标注始终使用原图像素坐标，导出不依赖预览缩放和显示器 DPI。
        var result = new RenderTargetBitmap(original.PixelWidth, original.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    public static void SavePng(BitmapSource image, string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请使用 .png 扩展名保存截图。");
        var tempPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoder.Save(file);
                file.Flush(true);
            }
            if (File.Exists(path)) File.Replace(tempPath, path, path + ".bak");
            else File.Move(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
