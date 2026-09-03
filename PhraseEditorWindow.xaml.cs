using System.Windows;

namespace FloatingPhrases;

public partial class PhraseEditorWindow : Window
{
    public PhraseEditorWindow(Phrase? phrase = null)
    {
        InitializeComponent();

        if (phrase is not null)
        {
            TitleBox.Text = phrase.Title;
            GroupBox.Text = phrase.Group;
            ContentBox.Text = phrase.Text;
            PinnedBox.IsChecked = phrase.IsPinned;
        }
        else
        {
            GroupBox.Text = "常用";
        }

        Loaded += (_, _) => ContentBox.Focus();
    }

    public string PhraseTitle => TitleBox.Text.Trim();
    public string PhraseGroup => GroupBox.Text.Trim();
    public string PhraseText => ContentBox.Text;
    public bool IsPinned => PinnedBox.IsChecked == true;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PhraseText))
        {
            System.Windows.MessageBox.Show(this, "短语内容不能为空。", "Floating Phrases", MessageBoxButton.OK, MessageBoxImage.Information);
            ContentBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
