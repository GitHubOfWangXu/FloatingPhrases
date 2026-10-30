using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FloatingPhrases;

internal static class ScreenshotEditorChecks
{
    public static void Run(string directory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { CheckEditor(directory); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("截图编辑检查失败", failure);
        Console.WriteLine("截图编辑检查通过：图形、DPI 与原图尺寸、撤销重做及 PNG 备份；未截图、读取剪贴板或操作用户文件。");
    }

    private static void CheckEditor(string directory)
    {
        var style = new DrawingAttributes { Color = Colors.Red, Width = 6, Height = 6 };
        var rectangle = ScreenshotDrawing.CreateShape(ScreenshotDrawingTool.Rectangle, new Point(700, 600), new Point(200, 100), style);
        Check(rectangle.StylusPoints.Count == 5 && rectangle.StylusPoints[0].X == 200 && rectangle.StylusPoints[0].Y == 100
            && rectangle.StylusPoints[2].X == 700 && rectangle.StylusPoints[2].Y == 600, "反向拖动画框应归一化边界并闭合");
        var line = ScreenshotDrawing.CreateShape(ScreenshotDrawingTool.Line, new Point(100, 300), new Point(1100, 300), style);
        var arrow = ScreenshotDrawing.CreateShape(ScreenshotDrawingTool.Arrow, new Point(500, 500), new Point(100, 200), style);
        Check(line.StylusPoints.Count == 2 && arrow.StylusPoints.Count == 5, "直线和箭头应保留正确的几何结构");
        var zeroArrow = ScreenshotDrawing.CreateShape(ScreenshotDrawingTool.Arrow, new Point(10, 10), new Point(10, 10), style);
        Check(zeroArrow.StylusPoints.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y)), "零长度箭头不能产生无效坐标");
        style.Color = Colors.Blue;
        Check(line.DrawingAttributes.Color == Colors.Red && !line.DrawingAttributes.FitToCurve, "后续修改颜色不能改变已绘制图形或把直线拟合成曲线");

        BitmapSource? original = null;
        foreach (var dpi in new[] { 96, 144, 192 })
        {
            original = WhiteImage(dpi);
            var rendered = ScreenshotDrawing.Render(original, new StrokeCollection { line });
            Check(rendered.PixelWidth == 1200 && rendered.PixelHeight == 800, "导出必须保持原图像素尺寸");
            Check(IsRed(rendered, 600, 300) && IsWhite(rendered, 600, 500), "不同 DPI 下标注应落在同一个原图像素位置");
            Check(IsWhite(original, 600, 300), "绘制和导出不能修改原图");
        }

        var window = new ScreenshotEditorWindow(original!);
        try
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(648, 420));
            root.Arrange(new Rect(0, 0, 648, 420));
            root.UpdateLayout();
            var canvas = (InkCanvas)window.FindName("AnnotationCanvas");
            var undo = (Button)window.FindName("UndoButton");
            var redo = (Button)window.FindName("RedoButton");
            canvas.Strokes.Add(line);
            canvas.Strokes.Add(rectangle);
            Check(undo.IsEnabled && !redo.IsEnabled && canvas.Strokes.Count == 2, "添加标注应启用撤销");
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(canvas.Strokes.Count == 1 && ReferenceEquals(canvas.Strokes[0], line) && redo.IsEnabled, "撤销应只移除最后一项");
            redo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(canvas.Strokes.Count == 2 && ReferenceEquals(canvas.Strokes[1], rectangle), "重做应恢复原标注及顺序");
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            canvas.Strokes.Add(arrow);
            Check(!redo.IsEnabled, "撤销后新建标注应清空重做分支");
            var toolBox = (ComboBox)window.FindName("ToolBox");
            toolBox.SelectedIndex = (int)ScreenshotDrawingTool.Pen;
            Check(canvas.EditingMode == InkCanvasEditingMode.Ink, "画笔应启用原生 InkCanvas 输入");
            toolBox.SelectedIndex = (int)ScreenshotDrawingTool.Rectangle;
            Check(canvas.EditingMode == InkCanvasEditingMode.None, "几何图形不能同时触发自由画笔");
            var image = window.RenderImage();
            Check(image.PixelWidth == 1200 && image.PixelHeight == 800 && IsRed(image, 600, 300), "缩小预览后导出仍应保持原始坐标和分辨率");
        }
        finally { window.Close(); }

        var path = Path.Combine(directory, "annotated.png");
        ScreenshotDrawing.SavePng(original!, path);
        var originalFile = File.ReadAllBytes(path);
        var marked = ScreenshotDrawing.Render(original!, new StrokeCollection { line });
        ScreenshotDrawing.SavePng(marked, path);
        Check(File.ReadAllBytes(path + ".bak").SequenceEqual(originalFile), "覆盖 PNG 前应保留原文件备份");
        using (var file = File.OpenRead(path))
        {
            var saved = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            Check(saved.PixelWidth == 1200 && saved.PixelHeight == 800 && IsRed(saved, 600, 300), "PNG 应保存最终标注和正确尺寸");
        }
        var invalidPath = Path.Combine(directory, "keep.txt");
        File.WriteAllText(invalidPath, "keep-original");
        try { ScreenshotDrawing.SavePng(marked, invalidPath); throw new Exception("非 PNG 目标应被拒绝"); }
        catch (InvalidOperationException) { }
        Check(File.ReadAllText(invalidPath) == "keep-original", "拒绝保存时不能修改原文件");
    }

    private static BitmapSource WhiteImage(double dpi)
    {
        var pixels = Enumerable.Repeat((byte)255, 1200 * 800 * 4).ToArray();
        var image = BitmapSource.Create(1200, 800, dpi, dpi, PixelFormats.Bgra32, null, pixels, 1200 * 4);
        image.Freeze();
        return image;
    }

    private static byte[] Pixel(BitmapSource image, int x, int y)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel;
    }
    private static bool IsRed(BitmapSource image, int x, int y) { var p = Pixel(image, x, y); return p[2] > 240 && p[1] < 20 && p[0] < 20; }
    private static bool IsWhite(BitmapSource image, int x, int y) => Pixel(image, x, y).All(channel => channel == 255);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
