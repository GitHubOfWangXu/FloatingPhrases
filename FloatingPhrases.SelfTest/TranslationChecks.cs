using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FloatingPhrases;

internal static class TranslationChecks
{
    public static void Run(string directory) => RunAsync(directory).GetAwaiter().GetResult();

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
