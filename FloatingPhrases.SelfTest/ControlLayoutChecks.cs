using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

internal static class ControlLayoutChecks
{
    private static bool Displayed(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement ui && ui.Visibility != Visibility.Visible) return false;
        return element.ActualHeight > 0;
    }

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    internal static void Check(FrameworkElement view, string scenario)
    {
        foreach (var input in Descendants<Control>(view).Where(c => Displayed(c) && c is TextBox or PasswordBox))
        {
            if (input.Template.FindName("PART_ContentHost", input) is not ScrollViewer host) continue;
            var line = new FormattedText("Ag中文", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch),
                input.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(input).PixelsPerDip);
            if (host.ViewportHeight + 0.5 < line.Height)
                throw new InvalidOperationException($"{scenario}: input {input.Name} clips a line ({host.ViewportHeight:F1} < {line.Height:F1})");
        }
        foreach (var button in Descendants<Control>(view).Where(b => Displayed(b)
                     && (b is Button { Content: string } or CheckBox { Content: string } or ComboBox)))
        {
            var text = Descendants<TextBlock>(button).FirstOrDefault();
            if (text is null) continue;
            var line = new FormattedText(text.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
            if (text.ActualWidth + 0.5 < line.Width || text.ActualHeight + 0.5 < line.Height)
                throw new InvalidOperationException($"{scenario}: {button.GetType().Name} {text.Text} clips text ({text.ActualWidth:F1}x{text.ActualHeight:F1}, needs {line.Width:F1}x{line.Height:F1})");
        }
    }

    internal static void ExportDialogs(string sourceRoot, string output)
    {
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var name in new[] { "AppEditorWindow", "FolderEditorWindow", "PhraseEditorWindow", "CommandEditorWindow",
                     "TranslationWindow", "ScreenshotEditorWindow", "ProcessMemoryWindow", "ScreenshotWindow" })
        {
            var doc = XDocument.Load(Path.Combine(sourceRoot, name + ".xaml"));
            var root = doc.Root!;
            var width = (double?)root.Attribute("Width") ?? 680;
            var height = (double?)root.Attribute("Height") ?? 480;
            var minWidth = (double?)root.Attribute("MinWidth") ?? width;
            var minHeight = (double?)root.Attribute("MinHeight") ?? height;
            root.Name = ui + "UserControl";
            root.SetAttributeValue(XNamespace.Xmlns + "local", "clr-namespace:FloatingPhrases;assembly=FloatingPhrases");
            root.SetAttributeValue("Style", "{DynamicResource {x:Type UserControl}}");
            foreach (var attr in root.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name != "Style" && a.Name != "Background").ToArray()) attr.Remove();
            foreach (var attr in root.DescendantsAndSelf().Attributes().Where(a => a.Value.EndsWith("_Click")
                         || a.Value.StartsWith("Window_") || a.Value.StartsWith("Canvas_")
                         || a.Name.LocalName is "TextChanged" or "SelectionChanged" or "LostMouseCapture").ToArray()) attr.Remove();
            root.AddFirst(new XElement(ui + "UserControl.Resources", new XElement(ui + "ResourceDictionary",
                new XAttribute("Source", "/FloatingPhrases;component/Themes/Shadcn.xaml"))));
            var view = (UserControl)XamlReader.Parse(doc.ToString());
            if (view.FindName("CategoryBox") is ComboBox category) { category.Items.Add("自定义"); category.SelectedIndex = 0; }
            foreach (var size in new[] { new Size(width, height - 32), new Size(minWidth, minHeight - 32) }.Distinct())
            foreach (var expanded in name == "TranslationWindow" ? new[] { false, true } : new[] { false })
            {
                view.Width = size.Width;
                view.Height = size.Height;
                view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
                foreach (var input in Descendants<TextBox>(view)) input.Text = "示例 Ag中文 123";
                foreach (var input in Descendants<PasswordBox>(view)) input.Password = "sample-key";
                foreach (var combo in Descendants<ComboBox>(view).Where(c => c.Items.Count > 0)) combo.SelectedIndex = 0;
                foreach (var expander in Descendants<Expander>(view)) expander.IsExpanded = expanded;
                view.UpdateLayout();
                var scenario = $"{name}-{size.Width}-{size.Height}-{expanded}";
                Check(view, scenario);
                if (name == "PhraseEditorWindow")
                {
                    var body = (TextBox)view.FindName("ContentBox");
                    var parent = (FrameworkElement)body.Parent;
                    var bounds = body.TransformToAncestor(parent).TransformBounds(new Rect(body.RenderSize));
                    if (bounds.Bottom < parent.ActualHeight - 0.5)
                        throw new InvalidOperationException($"{scenario}: phrase body must use the available editing area");
                }
                foreach (var control in Descendants<Control>(view).Where(c => Displayed(c) && c is Button or TextBox or ComboBox or CheckBox))
                {
                    var bounds = control.TransformToAncestor(view).TransformBounds(new Rect(control.RenderSize));
                    if (bounds.Left < -0.5 || bounds.Right > size.Width + 0.5 || bounds.Top < -0.5 || bounds.Bottom > size.Height + 0.5)
                        throw new InvalidOperationException($"{scenario}: {control.GetType().Name} {control.Name} outside window");
                }
                foreach (var dpi in new[] { 96, 144, 192 })
                {
                    var bitmap = new RenderTargetBitmap((int)(size.Width * dpi / 96), (int)(size.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"{scenario}-{dpi}.png")); encoder.Save(file);
                }
            }
        }
    }
}
