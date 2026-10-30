using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;

namespace FloatingPhrases;

public partial class TranslationWindow : Window
{
    private readonly TranslationService _service = new();
    private readonly TranslationKeyStore _keyStore = new();
    private string? _apiKey;
    private CancellationTokenSource? _request;

    public TranslationWindow()
    {
        InitializeComponent();
        try
        {
            _apiKey = _keyStore.Load();
            if (_apiKey is null) KeySettings.IsExpanded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            KeySettings.IsExpanded = true;
            StatusText.Text = "无法读取已保存的密钥，原文件保留。请重新填写并保存。";
        }
    }

    public void TranslateClipboard()
    {
        string text;
        try { text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : ""; }
        catch (ExternalException)
        {
            StatusText.Text = "剪贴板暂时被占用，请重新复制英文后重试。";
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText.Text = "剪贴板中没有文字，请先选中英文并复制。";
            return;
        }
        if (_request is not null && SourceBox.Text == text) return;
        _request?.Cancel();
        _request = null;
        TranslateButton.IsEnabled = true;
        CancelButton.IsEnabled = false;
        SourceBox.Text = text;
        ResultBox.Clear();
        CopyButton.IsEnabled = false;
        Translate_Click(this, new RoutedEventArgs());
    }

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceBox.Text))
        {
            TranslateClipboard();
            return;
        }
        if (SourceBox.Text.Length > TranslationService.MaxTextLength)
        {
            StatusText.Text = $"一次最多翻译 {TranslationService.MaxTextLength:N0} 个字符，请缩小选区后重新复制。";
            return;
        }
        if (string.IsNullOrEmpty(_apiKey))
        {
            KeySettings.IsExpanded = true;
            StatusText.Text = "请先填写并保存 API Key，再点击“翻译 / 重试”；当前未发送文本。";
            ApiKeyBox.Focus();
            return;
        }

        _request?.Cancel();
        using var request = new CancellationTokenSource();
        _request = request;
        ResultBox.Clear();
        CopyButton.IsEnabled = TranslateButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        StatusText.Text = "正在翻译…文本已提交至阿里云 Qwen-MT-Flash。";
        try
        {
            var result = await _service.TranslateAsync(SourceBox.Text, _apiKey, request.Token);
            if (_request != request || request.IsCancellationRequested) return;
            ResultBox.Text = result;
            CopyButton.IsEnabled = true;
            StatusText.Text = "翻译完成。译文仅在本窗口显示，未自动保存或替换剪贴板。";
        }
        catch (OperationCanceledException)
        {
            if (_request == request) StatusText.Text = request.IsCancellationRequested
                ? "已取消等待。已提交的请求可能仍会计费；原文保留，可手动重试。"
                : "翻译请求超时，原文保留，请检查网络后重试。";
        }
        catch (HttpRequestException)
        {
            if (_request == request) StatusText.Text = "无法连接翻译服务，原文保留，请检查网络后重试。";
        }
        catch (InvalidOperationException ex)
        {
            if (_request == request) StatusText.Text = ex.Message;
        }
        finally
        {
            if (_request == request)
            {
                _request = null;
                TranslateButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
            }
        }
    }

    private void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var key = ApiKeyBox.Password.Trim();
            _keyStore.Save(key);
            _apiKey = key;
            ApiKeyBox.Clear();
            KeySettings.IsExpanded = false;
            StatusText.Text = "密钥已加密保存。点击“翻译 / 重试”发送当前原文。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
        {
            StatusText.Text = "无法保存密钥，请检查输入和本机文件权限；原密钥与本次输入均保留。";
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ResultBox.Text)) return;
        try
        {
            System.Windows.Clipboard.SetText(ResultBox.Text);
            StatusText.Text = "中文译文已复制。";
        }
        catch (ExternalException) { StatusText.Text = "剪贴板暂时被占用，译文保留，请重试。"; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _request?.Cancel();

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _request?.Cancel();
        _request = null;
        _apiKey = null;
        ApiKeyBox.Clear();
        base.OnClosed(e);
    }
}
