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
    private readonly TranslationKeyStore _keyStore;
    private TranslationProvider _provider;
    private bool _providerReady;
    private string? _apiKey;
    private CancellationTokenSource? _request;

    public TranslationWindow() : this(new TranslationKeyStore()) { }

    public TranslationWindow(TranslationKeyStore keyStore)
    {
        _keyStore = keyStore;
        InitializeComponent();
        try
        {
            _provider = _keyStore.LoadProvider();
            _apiKey = _keyStore.Load(_provider);
            if (_apiKey is null) KeySettings.IsExpanded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
        {
            KeySettings.IsExpanded = true;
            StatusText.Text = "无法读取已保存的密钥，原文件保留。请重新填写并保存。";
        }
        ProviderBox.SelectedValue = _provider;
        UpdateKeyHint();
        _providerReady = true;
    }

    private string ProviderName => _provider switch
    {
        TranslationProvider.GlmCoding => "智谱 GLM Coding / GLM-5.3-Flash",
        TranslationProvider.SiliconFlowHunyuan => "硅基流动 Hunyuan-MT-7B",
        TranslationProvider.SiliconFlowGlm => "硅基流动 GLM-5.3-Flash（付费）",
        TranslationProvider.SiliconFlowQwen => "硅基流动 Qwen3.5-35B-A3B（付费）",
        _ => "阿里云 Qwen-MT-Flash"
    };

    private void UpdateKeyHint() => KeyHintText.Text = _provider switch
    {
        TranslationProvider.GlmCoding => "填写智谱 GLM Coding 套餐 API Key。套餐与客户端资格由服务端校验；失败不会自动转为普通 API 计费。密钥在本机单独加密保存。",
        TranslationProvider.Qwen => "填写百炼北京地域、按量付费 API Key；不要使用百炼 Coding Plan 密钥。密钥在本机单独加密保存。",
        TranslationProvider.SiliconFlowHunyuan => "填写硅基流动 API Key。Hunyuan-MT-7B 当前标为免费，实际价格和限额以硅基流动控制台为准。失败不自动切换付费模型。密钥在本机单独加密保存。",
        _ => "填写硅基流动 API Key，此选项会按硅基流动价格计费，与 GLM Coding 套餐独立。硅基流动模型共用本机加密密钥，切换不会自动发起翻译。"
    };

    private void ProviderBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_providerReady || ProviderBox.SelectedValue is not TranslationProvider selected || selected == _provider) return;
        string? failure = null;
        if (ApiKeyBox.Password.Length > 0) failure = "密钥框还有未保存输入，请先保存当前服务的密钥或清空输入，再切换服务。";
        else
        {
            try
            {
                string? key = null;
                var keyUnreadable = false;
                try { key = _keyStore.Load(selected); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
                {
                    keyUnreadable = true;
                }
                _keyStore.SaveProvider(selected);
                ResetTranslation();
                _provider = selected;
                _apiKey = key;
                UpdateKeyHint();
                KeySettings.IsExpanded = key is null;
                StatusText.Text = keyUnreadable
                    ? "无法读取所选服务的密钥，原文件保留，当前未发送文本。请重新填写并保存该服务的密钥。"
                    : $"已切换到 {ProviderName}，当前未发送文本；点击翻译开始。";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
            {
                failure = "无法切换翻译服务，请检查本机配置权限或该服务的密钥文件；原服务和原文保留。";
            }
        }
        if (failure is null) return;
        _providerReady = false;
        ProviderBox.SelectedValue = _provider;
        _providerReady = true;
        StatusText.Text = failure;
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
            StatusText.Text = "剪贴板中没有文字，也可在上方直接输入或粘贴英文后点击翻译。";
            SourceBox.Focus();
            return;
        }
        if (_request is not null && SourceBox.Text == text) return;
        SourceBox.Text = text;
        Translate_Click(this, new RoutedEventArgs());
    }

    private void SourceBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsInitialized) return;
        ResetTranslation();
        StatusText.Text = "原文已修改，点击“翻译 / 重试”提交；当前未发送文本。";
    }

    private void ResetTranslation()
    {
        _request?.Cancel();
        _request = null;
        ResultBox.Clear();
        CopyButton.IsEnabled = CancelButton.IsEnabled = false;
        TranslateButton.IsEnabled = true;
    }

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceBox.Text))
        {
            StatusText.Text = "请输入或粘贴需要翻译的英文。";
            SourceBox.Focus();
            return;
        }
        if (SourceBox.Text.Length > TranslationService.MaxTextLength)
        {
            StatusText.Text = $"一次最多翻译 {TranslationService.MaxTextLength:N0} 个字符，请缩短原文后重试。";
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
        StatusText.Text = $"正在翻译…文本提交至 {ProviderName}。";
        try
        {
            var result = await _service.TranslateAsync(SourceBox.Text, _apiKey, request.Token, _provider);
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
            _keyStore.Save(key, _provider);
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
