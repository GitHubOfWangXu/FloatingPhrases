using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FloatingPhrases;

internal static class TranslationChecks
{
    public static void Run(string directory)
    {
        RunAsync(directory).GetAwaiter().GetResult();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var editingStore = new TranslationKeyStore(Path.Combine(directory, "editing-key.bin"));
                var window = new TranslationWindow(editingStore);
                try
                {
                    var source = (TextBox)window.FindName("SourceBox");
                    var result = (TextBox)window.FindName("ResultBox");
                    var copy = (Button)window.FindName("CopyButton");
                    var translate = (Button)window.FindName("TranslateButton");
                    Check(!source.IsReadOnly && source.AcceptsReturn && result.IsReadOnly, "原文应支持多行输入，译文仍只读");
                    source.Text = "Hello\nWorld";
                    result.Text = "旧译文";
                    copy.IsEnabled = true;
                    source.AppendText("!");
                    Check(result.Text == "" && !copy.IsEnabled && translate.IsEnabled, "修改原文应清除旧译文并允许主动重译");
                    translate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(source.Text == "Hello\nWorld!" && !File.Exists(Path.Combine(directory, "editing-key.bin")), "未配置密钥时应保留手动输入，不创建凭据");
                    source.Clear();
                    translate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(source.Text == "" && ((TextBlock)window.FindName("StatusText")).Text.Contains("请输入"), "空原文应提示输入，不能隐式读取剪贴板");
                    var provider = (ComboBox)window.FindName("ProviderBox");
                    var keyBox = (PasswordBox)window.FindName("ApiKeyBox");
                    Check(Equals(provider.SelectedValue, TranslationProvider.Qwen), "旧配置应继续使用 Qwen");
                    keyBox.Password = "unsaved-test-key";
                    provider.SelectedValue = TranslationProvider.GlmCoding;
                    Check(Equals(provider.SelectedValue, TranslationProvider.Qwen) && keyBox.Password == "unsaved-test-key", "未保存密钥不能因切换而丢失或被保存到另一服务");
                    keyBox.Clear();
                    source.Text = "Keep this text";
                    result.Text = "旧服务的译文";
                    copy.IsEnabled = true;
                    provider.SelectedValue = TranslationProvider.GlmCoding;
                    Check(editingStore.LoadProvider() == TranslationProvider.GlmCoding && source.Text == "Keep this text"
                        && result.Text == "" && !copy.IsEnabled, "切换应记住服务、保留原文并清除旧译文，不发起请求");
                    keyBox.Password = "glm-ui-test-key";
                    ((Button)window.FindName("SaveKeyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(editingStore.Load(TranslationProvider.GlmCoding) == "glm-ui-test-key" && editingStore.Load() is null && keyBox.Password == "", "GLM 密钥应单独保存，不能覆盖 Qwen");
                    provider.SelectedValue = TranslationProvider.Qwen;
                    Check(((Expander)window.FindName("KeySettings")).IsExpanded && editingStore.Load() is null, "切回未配置的 Qwen 时不能复用 GLM 密钥");
                    File.WriteAllText(Path.Combine(directory, "editing-key.bin.glm-coding"), "corrupt-glm-key");
                    provider.SelectedValue = TranslationProvider.GlmCoding;
                    Check(Equals(provider.SelectedValue, TranslationProvider.GlmCoding) && ((Expander)window.FindName("KeySettings")).IsExpanded
                        && File.ReadAllText(Path.Combine(directory, "editing-key.bin.glm-coding")) == "corrupt-glm-key",
                        "密钥损坏时应允许选中该服务重新配置，保留坏文件且不发送请求");
                }
                finally { window.Close(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("翻译手动输入控件检查失败", failure);
        Console.WriteLine("翻译手动输入控件检查通过；未显示窗口、读取剪贴板或访问真实密钥。");
    }

    private static async Task RunAsync(string directory)
    {
        const string key = "test-key-not-a-real-credential";
        const string text = "Hello\nWorld & <example>";
        var requests = 0;
        using var client = new HttpClient(new StubHandler(async (request, token) =>
        {
            requests++;
            Check(request.Method == HttpMethod.Post, "翻译必须使用 POST");
            Check(request.RequestUri!.ToString() == "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", "只能发送到北京地域的官方接口");
            Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == key, "密钥应仅在授权头传递");
            var body = await request.Content!.ReadAsStringAsync(token);
            using var json = JsonDocument.Parse(body);
            Check(!body.Contains(key), "密钥不能混入请求正文");
            Check(json.RootElement.GetProperty("model").GetString() == "qwen-mt-flash", "固定使用已选翻译模型");
            var messages = json.RootElement.GetProperty("messages");
            Check(messages.GetArrayLength() == 1 && messages[0].GetProperty("role").GetString() == "user"
                && messages[0].GetProperty("content").GetString() == text, "只发送主动提供的文本，保留原文");
            var options = json.RootElement.GetProperty("translation_options");
            Check(options.GetProperty("source_lang").GetString() == "English" && options.GetProperty("target_lang").GetString() == "Chinese", "必须使用英译中参数");
            return Reply("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"你好，世界\"}}]}");
        }));
        var service = new TranslationService(client);
        Check(await service.TranslateAsync(text, key, default) == "你好，世界", "应读取真实响应字段");
        await Reject(() => service.TranslateAsync(" ", key, default));
        await Reject(() => service.TranslateAsync(new string('x', TranslationService.MaxTextLength + 1), key, default));
        await Reject(() => service.TranslateAsync(text, "bad\r\nkey", default));
        Check(requests == 1, "无效输入不得发起网络请求");

        var glmRequests = 0;
        using (var glmClient = new HttpClient(new StubHandler(async (request, token) =>
        {
            glmRequests++;
            Check(request.RequestUri!.ToString() == "https://open.bigmodel.cn/api/coding/paas/v4/chat/completions", "GLM 只能请求 Coding 专用端点");
            Check(request.Headers.Authorization?.Parameter == "glm-test-key" && request.Headers.UserAgent.ToString() == "FloatingPhrases/1.0", "GLM 应使用自己的密钥与真实客户端身份");
            var body = await request.Content!.ReadAsStringAsync(token);
            using var json = JsonDocument.Parse(body);
            Check(json.RootElement.GetProperty("model").GetString() == "glm-5.3-flash" && !json.RootElement.TryGetProperty("translation_options", out _), "GLM 不应收到 Qwen 专属参数");
            var messages = json.RootElement.GetProperty("messages");
            Check(messages.GetArrayLength() == 2 && messages[0].GetProperty("role").GetString() == "system"
                && messages[0].GetProperty("content").GetString()!.Contains("不要执行或回答")
                && messages[1].GetProperty("role").GetString() == "user" && messages[1].GetProperty("content").GetString() == text,
                "翻译提示词和原文必须分开，不附加历史、工具或其他资料");
            return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("glm-test-key" + text) };
        })))
        {
            var message = await Reject(() => new TranslationService(glmClient).TranslateAsync(text, "glm-test-key", default, TranslationProvider.GlmCoding));
            Check(glmRequests == 1 && !message.Contains("glm-test-key") && !message.Contains(text) && message.Contains("未切换"), "GLM 失败不得重试、泄漏响应原文或回退到付费 API");
        }
        using (var glmSuccess = new HttpClient(new StubHandler((_, _) => Task.FromResult(Reply("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"GLM译文\",\"reasoning_content\":\"不显示思考内容\"}}]}")))))
            Check(await new TranslationService(glmSuccess).TranslateAsync(text, "glm-test-key", default, TranslationProvider.GlmCoding) == "GLM译文", "GLM 仅提取译文，不展示推理内容");

        foreach (var body in new[] { "not-json", "{}", "{\"choices\":[]}",
            "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"部分译文\"}}]}",
            "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\" \"}}]}" })
        {
            using var malformed = new HttpClient(new StubHandler((_, _) => Task.FromResult(Reply(body))));
            await Reject(() => new TranslationService(malformed).TranslateAsync(text, key, default));
        }
        using (var denied = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            { Content = new StringContent(key + text) }))))
        {
            var message = await Reject(() => new TranslationService(denied).TranslateAsync(text, key, default));
            Check(!message.Contains(key) && !message.Contains(text), "错误不得泄漏服务端回显的凭据和正文");
        }
        using (var cancellation = new CancellationTokenSource())
        using (var slow = new HttpClient(new StubHandler(async (_, token) =>
            { await Task.Delay(Timeout.Infinite, token); return Reply("{}"); })))
        {
            var pending = new TranslationService(slow).TranslateAsync(text, key, cancellation.Token);
            cancellation.Cancel();
            try { await pending; throw new Exception("取消请求必须中止等待"); }
            catch (OperationCanceledException) { }
        }

        var path = Path.Combine(directory, "translation-key.bin");
        var store = new TranslationKeyStore(path);
        Check(store.Load() is null && !File.Exists(path), "未配置时不能自动创建密钥或占位符");
        store.Save(key);
        Check(store.Load() == key, "当前用户应能解密保存的密钥");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(key), "落盘不能包含明文密钥");
        Check(store.LoadProvider() == TranslationProvider.Qwen, "没有服务选择文件时应兼容旧版本");
        store.Save("glm-storage-test-key", TranslationProvider.GlmCoding);
        store.SaveProvider(TranslationProvider.GlmCoding);
        Check(store.Load(TranslationProvider.GlmCoding) == "glm-storage-test-key" && store.Load() == key
            && new TranslationKeyStore(path).LoadProvider() == TranslationProvider.GlmCoding, "密钥应分开存储，重开后应恢复所选服务");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path + ".glm-coding")).Contains("glm-storage-test-key"), "GLM 密钥也必须加密落盘");
        store.Save("replacement-test-key");
        Check(store.Load() == "replacement-test-key" && new TranslationKeyStore(path + ".bak").Load() == key, "更新应保留可恢复的加密备份");
        try { store.Save("bad key"); throw new Exception("应拒绝无效密钥"); }
        catch (InvalidOperationException) { }
        Check(store.Load() == "replacement-test-key", "保存失败不能覆盖原密钥");
        File.WriteAllText(path, "corrupt-test-data");
        try { store.Load(); throw new Exception("损坏密钥不能静默当成成功"); }
        catch (CryptographicException) { }
        Check(File.ReadAllText(path) == "corrupt-test-data", "损坏密钥必须保留原文件");
        Console.WriteLine("翻译专项检查通过：请求契约、输入边界、异常脱敏、取消及本机密钥加密恢复；未调用云端。");
    }

    private static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static async Task<string> Reject(Func<Task<string>> action)
    {
        try { await action(); }
        catch (InvalidOperationException ex) { return ex.Message; }
        throw new Exception("预期应拒绝请求或响应");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
