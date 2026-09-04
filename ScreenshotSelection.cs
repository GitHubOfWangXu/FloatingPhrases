namespace FloatingPhrases;

public readonly record struct ScreenshotRectangle(int X, int Y, int Width, int Height);

public static class ScreenshotSelection
{
    public static ScreenshotRectangle? ToPixels(
        double x1,
        double y1,
        double x2,
        double y2,
        double scaleX,
        double scaleY,
        int pixelWidth,
        int pixelHeight)
    {
        if (scaleX <= 0 || scaleY <= 0 || pixelWidth <= 0 || pixelHeight <= 0)
        {
            return null;
        }

        var left = Math.Clamp((int)Math.Floor(Math.Min(x1, x2) * scaleX), 0, pixelWidth);
        var top = Math.Clamp((int)Math.Floor(Math.Min(y1, y2) * scaleY), 0, pixelHeight);
        var right = Math.Clamp((int)Math.Ceiling(Math.Max(x1, x2) * scaleX), 0, pixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(Math.Max(y1, y2) * scaleY), 0, pixelHeight);

        return right > left && bottom > top
            ? new ScreenshotRectangle(left, top, right - left, bottom - top)
            : null;
    }
}
