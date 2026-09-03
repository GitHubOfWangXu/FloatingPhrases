using System.Drawing;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;
using Rect = System.Windows.Rect;

namespace FloatingPhrases;

internal static class FeishuSelectionReader
{
    private const string HiddenSelectionClass = "docx-selection-hidden-textarea";
    private const double CandidateHighlightRatio = 0.2;
    private const double CharacterHighlightRatio = 0.1;

    public static bool TryRead(AutomationElement focusedElement, out string text)
    {
        text = string.Empty;
        if (!string.Equals(focusedElement.Current.ClassName, HiddenSelectionClass, StringComparison.Ordinal))
        {
            return false;
        }

        var document = FindDocument(focusedElement);
        if (document is null
            || !document.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)
            || pattern is not TextPattern textPattern)
        {
            return false;
        }

        var virtualScreen = Forms.SystemInformation.VirtualScreen;
        using var screen = new Bitmap(virtualScreen.Width, virtualScreen.Height);
        using (var graphics = Graphics.FromImage(screen))
        {
            graphics.CopyFromScreen(virtualScreen.Location, DrawingPoint.Empty, virtualScreen.Size);
        }

        var parts = ReadHighlightedParts(document, textPattern, screen, virtualScreen.Location);
        text = Assemble(parts).TrimEnd();
        return !string.IsNullOrWhiteSpace(text);
    }

    public static bool TryReadFromDesktop(out string text)
    {
        text = string.Empty;
        var candidates = AutomationElement.RootElement.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ClassNameProperty, HiddenSelectionClass));

        foreach (var candidate in candidates.Cast<AutomationElement>())
        {
            if (TryRead(candidate, out text))
            {
                return true;
            }
        }

        return false;
    }

    private static AutomationElement? FindDocument(AutomationElement element)
    {
        for (var depth = 0; element is not null && depth < 12; depth++)
        {
            if (element.Current.ControlType == ControlType.Document
                && element.Current.AutomationId == "RootWebArea")
            {
                return element;
            }

            element = TreeWalker.ControlViewWalker.GetParent(element);
        }

        return null;
    }

    private static List<HighlightedPart> ReadHighlightedParts(
        AutomationElement document,
        TextPattern textPattern,
        Bitmap screen,
        DrawingPoint screenOrigin)
    {
        var parts = new List<HighlightedPart>();
        var textualControls = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink));
        var descendants = document.FindAll(TreeScope.Descendants, textualControls);

        foreach (var element in descendants.Cast<AutomationElement>())
        {
            if (element.FindFirst(TreeScope.Descendants, textualControls) is not null)
            {
                continue;
            }

            var range = textPattern.RangeFromChild(element);
            var bounds = range.GetBoundingRectangles();
            if (bounds.Length == 0
                || !bounds.Any(rectangle => SelectionColorRatio(screen, screenOrigin, rectangle) >= CandidateHighlightRatio))
            {
                continue;
            }

            var part = ReadHighlightedPart(range, bounds, screen, screenOrigin);
            if (part is not null)
            {
                parts.Add(part);
            }
        }

        return parts;
    }

    private static HighlightedPart? ReadHighlightedPart(
        TextPatternRange range,
        Rect[] bounds,
        Bitmap screen,
        DrawingPoint screenOrigin)
    {
        if (bounds.All(rectangle => IsHighlightedRectangle(screen, screenOrigin, rectangle)))
        {
            var completeText = range.GetText(-1);
            return completeText.Length == 0 ? null : new HighlightedPart(completeText, Union(bounds));
        }

        var selectedText = new StringBuilder();
        var selectedBounds = new List<Rect>();
        var cursor = range.Clone();
        cursor.MoveEndpointByRange(TextPatternRangeEndpoint.End, cursor, TextPatternRangeEndpoint.Start);

        for (var count = 0; count < 10_000; count++)
        {
            var character = cursor.Clone();
            if (character.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1) == 0)
            {
                break;
            }

            var characterBounds = character.GetBoundingRectangles();
            if (characterBounds.Any(rectangle =>
                SelectionColorRatio(screen, screenOrigin, rectangle) >= CharacterHighlightRatio))
            {
                selectedText.Append(character.GetText(-1));
                selectedBounds.AddRange(characterBounds);
            }

            if (cursor.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, 1) == 0)
            {
                break;
            }

            cursor.MoveEndpointByRange(TextPatternRangeEndpoint.End, cursor, TextPatternRangeEndpoint.Start);
        }

        return selectedText.Length == 0 || selectedBounds.Count == 0
            ? null
            : new HighlightedPart(selectedText.ToString(), Union(selectedBounds));
    }

    private static string Assemble(IEnumerable<HighlightedPart> parts)
    {
        var output = new StringBuilder();
        Rect? previousBounds = null;

        foreach (var part in parts)
        {
            if (previousBounds is { } previous)
            {
                var sameLine = Math.Abs(previous.Top - part.Bounds.Top)
                    <= Math.Min(previous.Height, part.Bounds.Height) / 2;
                if (!sameLine)
                {
                    TrimLineEnd(output);
                    output.AppendLine();
                }
                else if (part.Bounds.Left - previous.Right > Math.Min(previous.Height, part.Bounds.Height) * 0.25
                    && output.Length > 0
                    && !char.IsWhiteSpace(output[^1])
                    && !char.IsWhiteSpace(part.Text[0]))
                {
                    output.Append(' ');
                }
            }

            output.Append(part.Text);
            previousBounds = part.Bounds;
        }

        return output.ToString();
    }

    private static void TrimLineEnd(StringBuilder text)
    {
        while (text.Length > 0 && text[^1] is ' ' or '\t')
        {
            text.Length--;
        }
    }

    private static bool IsHighlightedRectangle(Bitmap screen, DrawingPoint origin, Rect bounds)
    {
        var points = new[]
        {
            new DrawingPoint((int)bounds.Left - origin.X + 1, (int)bounds.Top - origin.Y + 1),
            new DrawingPoint((int)bounds.Right - origin.X - 2, (int)bounds.Top - origin.Y + 1),
            new DrawingPoint((int)bounds.Left - origin.X + 1, (int)bounds.Bottom - origin.Y - 2),
            new DrawingPoint((int)bounds.Right - origin.X - 2, (int)bounds.Bottom - origin.Y - 2)
        };

        return points.Count(point => IsOnScreen(screen, point) && IsSelectionColor(screen.GetPixel(point.X, point.Y))) >= 3;
    }

    private static double SelectionColorRatio(Bitmap screen, DrawingPoint origin, Rect bounds)
    {
        var left = Math.Max(0, (int)Math.Floor(bounds.Left) - origin.X);
        var top = Math.Max(0, (int)Math.Floor(bounds.Top) - origin.Y);
        var right = Math.Min(screen.Width - 1, (int)Math.Ceiling(bounds.Right) - origin.X);
        var bottom = Math.Min(screen.Height - 1, (int)Math.Ceiling(bounds.Bottom) - origin.Y);
        var matches = 0;
        var samples = 0;

        for (var y = top; y <= bottom; y += 2)
        {
            for (var x = left; x <= right; x += 2)
            {
                samples++;
                if (IsSelectionColor(screen.GetPixel(x, y)))
                {
                    matches++;
                }
            }
        }

        return samples == 0 ? 0 : (double)matches / samples;
    }

    private static bool IsSelectionColor(Color color) => Math.Abs(color.R - 0xD5) <= 3
        && Math.Abs(color.G - 0xE1) <= 3
        && Math.Abs(color.B - 0xFC) <= 3;

    private static bool IsOnScreen(Bitmap screen, DrawingPoint point) =>
        point.X >= 0 && point.X < screen.Width && point.Y >= 0 && point.Y < screen.Height;

    private static Rect Union(IEnumerable<Rect> rectangles)
    {
        var result = Rect.Empty;
        foreach (var rectangle in rectangles)
        {
            result.Union(rectangle);
        }

        return result;
    }

    private sealed record HighlightedPart(string Text, Rect Bounds);
}
