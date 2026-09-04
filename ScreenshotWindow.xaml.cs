using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace FloatingPhrases;

public partial class ScreenshotWindow : Window
{
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly Rectangle _screenBounds;
    private readonly BitmapSource _screenImage;
    private System.Windows.Point _selectionStart;
    private bool _isSelecting;

    public ScreenshotWindow(BitmapSource screenImage, Rectangle screenBounds)
    {
        InitializeComponent();
        _screenImage = screenImage;
        _screenBounds = screenBounds;
        ScreenImage.Source = screenImage;

        Left = screenBounds.Left;
        Top = screenBounds.Top;
        Width = screenBounds.Width;
        Height = screenBounds.Height;
        SourceInitialized += PositionOnScreen;
    }

    public BitmapSource? SelectedImage { get; private set; }

    public static BitmapSource CaptureScreen(Rectangle bounds)
    {
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        }

        var handle = bitmap.GetHbitmap();
        try
        {
            var image = Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    private void PositionOnScreen(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(
            handle,
            HwndTopmost,
            _screenBounds.Left,
            _screenBounds.Top,
            _screenBounds.Width,
            _screenBounds.Height,
            SwpShowWindow);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _selectionStart = e.GetPosition(SelectionCanvas);
        _isSelecting = true;
        SelectionBorder.Visibility = Visibility.Visible;
        SelectionCanvas.CaptureMouse();
        UpdateSelection(_selectionStart);
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isSelecting)
        {
            UpdateSelection(e.GetPosition(SelectionCanvas));
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        var end = e.GetPosition(SelectionCanvas);
        _isSelecting = false;
        SelectionCanvas.ReleaseMouseCapture();

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            DialogResult = false;
            return;
        }

        var rectangle = ScreenshotSelection.ToPixels(
            _selectionStart.X,
            _selectionStart.Y,
            end.X,
            end.Y,
            _screenImage.PixelWidth / ActualWidth,
            _screenImage.PixelHeight / ActualHeight,
            _screenImage.PixelWidth,
            _screenImage.PixelHeight);

        if (rectangle is not { Width: >= 3, Height: >= 3 } selected)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var image = new CroppedBitmap(
            _screenImage,
            new Int32Rect(selected.X, selected.Y, selected.Width, selected.Height));
        image.Freeze();
        SelectedImage = image;
        DialogResult = true;
    }

    private void UpdateSelection(System.Windows.Point current)
    {
        var left = Math.Min(_selectionStart.X, current.X);
        var top = Math.Min(_selectionStart.Y, current.Y);
        var width = Math.Abs(current.X - _selectionStart.X);
        var height = Math.Abs(current.Y - _selectionStart.Y);

        Canvas.SetLeft(SelectionBorder, left);
        Canvas.SetTop(SelectionBorder, top);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
