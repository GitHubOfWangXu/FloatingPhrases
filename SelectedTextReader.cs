using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace FloatingPhrases;

public static class SelectedTextReader
{
    private const int MaxAncestorDepth = 12;

    public static bool TryRead(out string text)
    {
        text = string.Empty;

        try
        {
            var element = AutomationElement.FocusedElement;
            var focusedElement = element;
            for (var depth = 0; element is not null && depth < MaxAncestorDepth; depth++)
            {
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)
                    && pattern is TextPattern textPattern)
                {
                    text = CombineSelections(textPattern.GetSelection().Select(range => range.GetText(-1)));
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return true;
                    }
                }

                element = TreeWalker.ControlViewWalker.GetParent(element);
            }

            if (focusedElement is not null && FeishuSelectionReader.TryRead(focusedElement, out text))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException
            or InvalidOperationException
            or ExternalException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            // The focused control can disappear while a global hotkey is being handled.
        }

        return false;
    }

    public static string CombineSelections(IEnumerable<string?> selections) =>
        string.Join(Environment.NewLine, selections.Where(value => !string.IsNullOrEmpty(value)));

    public static bool TryReadFeishuFromDesktop(out string text)
    {
        try
        {
            return FeishuSelectionReader.TryReadFromDesktop(out text);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException
            or InvalidOperationException
            or ExternalException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            text = string.Empty;
            return false;
        }
    }
}
