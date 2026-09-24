namespace FloatingPhrases;

public enum DockEdge { None, Left, Right, Top }

// All coordinates use the same screen-pixel space, including negative monitor origins.
public readonly record struct DockBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
}

public static class EdgeDockLayout
{
    public static DockEdge Detect(DockBounds window, DockBounds workArea, double threshold)
    {
        var candidates = new[]
        {
            (DockEdge.Left, Math.Abs(window.X - workArea.X)),
            (DockEdge.Right, Math.Abs(window.Right - workArea.Right)),
            (DockEdge.Top, Math.Abs(window.Y - workArea.Y))
        };
        var nearest = candidates.OrderBy(item => item.Item2).First();
        return nearest.Item2 <= threshold ? nearest.Item1 : DockEdge.None;
    }

    public static DockBounds Snap(DockBounds window, DockBounds area, DockEdge edge)
    {
        var x = Math.Clamp(window.X, area.X, Math.Max(area.X, area.Right - window.Width));
        var y = Math.Clamp(window.Y, area.Y, Math.Max(area.Y, area.Bottom - window.Height));
        return window with
        {
            X = edge == DockEdge.Left ? area.X : edge == DockEdge.Right ? area.Right - window.Width : x,
            Y = edge == DockEdge.Top ? area.Y : y
        };
    }

    public static DockBounds Handle(DockBounds expanded, DockBounds area, DockEdge edge, double scale)
    {
        var horizontal = edge == DockEdge.Top;
        var width = (horizontal ? 64 : 44) * scale;
        var height = (horizontal ? 40 : 64) * scale;
        return Snap(new DockBounds(
            expanded.X + (expanded.Width - width) / 2,
            expanded.Y + (expanded.Height - height) / 2, width, height), area, edge);
    }

    public static DockBounds PanelFromHandle(DockBounds handle, DockBounds panel, DockBounds area, DockEdge edge) =>
        Snap(panel with
        {
            X = handle.X + (handle.Width - panel.Width) / 2,
            Y = handle.Y + (handle.Height - panel.Height) / 2
        }, area, edge);
}
