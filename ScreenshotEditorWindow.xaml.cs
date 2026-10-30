using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace FloatingPhrases;

public partial class ScreenshotEditorWindow : Window
{
    private readonly BitmapSource _original;
    private readonly Stack<(StrokeCollection Added, StrokeCollection Removed)> _undo = new();
    private readonly Stack<(StrokeCollection Added, StrokeCollection Removed)> _redo = new();
    private bool _restoringHistory;
    private Point? _shapeStart;
    private Stroke? _previewStroke;

    public ScreenshotEditorWindow(BitmapSource original)
    {
        _original = original;
        InitializeComponent();
        OriginalImage.Source = original;
        DrawingSurface.Width = original.PixelWidth;
        DrawingSurface.Height = original.PixelHeight;
        AnnotationCanvas.Strokes.StrokesChanged += Strokes_Changed;
        ToolBox.SelectionChanged += (_, _) => ApplyTool();
        ColorBox.SelectionChanged += (_, _) => ApplyTool();
        WidthBox.SelectionChanged += (_, _) => ApplyTool();
        ApplyTool();
    }

    private ScreenshotDrawingTool CurrentTool => (ScreenshotDrawingTool)ToolBox.SelectedIndex;

    private void ApplyTool()
    {
        CancelPreview();
        var width = double.Parse((string)WidthBox.SelectedValue, CultureInfo.InvariantCulture);
        AnnotationCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = (Color)ColorConverter.ConvertFromString((string)ColorBox.SelectedValue),
            Width = width, Height = width, IgnorePressure = true, FitToCurve = true
        };
        AnnotationCanvas.EditingMode = CurrentTool == ScreenshotDrawingTool.Pen ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
    }

    private Point ImagePoint(System.Windows.Input.MouseEventArgs e)
    {
        var point = e.GetPosition(AnnotationCanvas);
        return new Point(Math.Clamp(point.X, 0, _original.PixelWidth - 1), Math.Clamp(point.Y, 0, _original.PixelHeight - 1));
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (CurrentTool == ScreenshotDrawingTool.Pen) return;
        _shapeStart = ImagePoint(e);
        AnnotationCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_shapeStart is not { } start) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CancelPreview(); return; }
        UpdatePreview(start, ImagePoint(e));
        e.Handled = true;
    }

    private void UpdatePreview(Point start, Point end)
    {
        _previewStroke = ScreenshotDrawing.CreateShape(CurrentTool, start, end, AnnotationCanvas.DefaultDrawingAttributes);
        ShapePreview.Points = new PointCollection(_previewStroke.StylusPoints.Select(point => new Point(point.X, point.Y)));
        ShapePreview.Stroke = new SolidColorBrush(_previewStroke.DrawingAttributes.Color);
        ShapePreview.StrokeThickness = _previewStroke.DrawingAttributes.Width;
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_shapeStart is not { } start) return;
        var end = ImagePoint(e);
        if ((end - start).Length >= 1)
        {
            UpdatePreview(start, end);
            var stroke = _previewStroke!;
            CancelPreview();
            AnnotationCanvas.Strokes.Add(stroke);
        }
        else CancelPreview();
        e.Handled = true;
    }

    private void Canvas_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) => CancelPreview();

    private void CancelPreview()
    {
        _shapeStart = null;
        _previewStroke = null;
        ShapePreview.Points.Clear();
        if (AnnotationCanvas.IsMouseCaptured && AnnotationCanvas.EditingMode == InkCanvasEditingMode.None)
            AnnotationCanvas.ReleaseMouseCapture();
    }

    private void Strokes_Changed(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (_restoringHistory) return;
        // 记录笔迹增删，避免每一步都复制整张截图或全部历史笔迹。
        _undo.Push((new StrokeCollection(e.Added), new StrokeCollection(e.Removed)));
        _redo.Clear();
        UpdateHistoryButtons();
        StatusText.Text = $"已添加标注，共 {AnnotationCanvas.Strokes.Count} 项。";
    }

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => RestoreHistory(_undo, _redo, false);
    private void Redo_Click(object sender, RoutedEventArgs e) => RestoreHistory(_redo, _undo, true);

    private void RestoreHistory(Stack<(StrokeCollection Added, StrokeCollection Removed)> from,
        Stack<(StrokeCollection Added, StrokeCollection Removed)> to, bool redo)
    {
        if (from.Count == 0 || AnnotationCanvas.IsMouseCaptured) return;
        var change = from.Pop();
        _restoringHistory = true;
        try
        {
            AnnotationCanvas.Strokes.Remove(redo ? change.Removed : change.Added);
            AnnotationCanvas.Strokes.Add(redo ? change.Added : change.Removed);
            to.Push(change);
        }
        finally { _restoringHistory = false; UpdateHistoryButtons(); }
        StatusText.Text = redo ? "已重做。" : "已撤销。";
    }

    public BitmapSource RenderImage() => ScreenshotDrawing.Render(_original, AnnotationCanvas.Strokes);

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (AnnotationCanvas.IsMouseCaptured) return;
        try
        {
            System.Windows.Clipboard.SetImage(RenderImage());
            DialogResult = true;
        }
        catch (ExternalException) { StatusText.Text = "剪贴板暂时被占用，标注已保留，请再次点击“完成并复制”。"; }
        catch (InvalidOperationException) { StatusText.Text = "暂时无法生成或复制截图，标注已保留，请重试。"; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (AnnotationCanvas.IsMouseCaptured) return;
        var picker = new Microsoft.Win32.SaveFileDialog
        {
            Title = "另存标注截图", Filter = "PNG 图片 (*.png)|*.png", DefaultExt = ".png",
            FileName = $"截图-{DateTime.Now:yyyyMMdd-HHmmss}.png", AddExtension = true, OverwritePrompt = true
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            ScreenshotDrawing.SavePng(RenderImage(), picker.FileName);
            StatusText.Text = "PNG 已保存。可以继续编辑，或点击“完成并复制”。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            StatusText.Text = "无法保存 PNG，原文件和标注保留，请选择可写的位置后重试。";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_shapeStart is not null) CancelPreview();
            else DialogResult = false;
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y)
        {
            if (e.Key == Key.Z) Undo_Click(this, e); else Redo_Click(this, e);
            e.Handled = true;
        }
    }
}
