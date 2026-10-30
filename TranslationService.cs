using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FloatingPhrases;

public sealed class TranslationService
{
    public const int MaxTextLength = 12000;
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(45),
        MaxResponseContentBufferSize = 1024 * 1024
    };
    private readonly HttpClient _client;

    public TranslationService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<string> TranslateAsync(string text, string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("请先选中英文并复制，再按 Ctrl+Alt+Y。");
        if (text.Length > MaxTextLength) throw new InvalidOperationException($"一次最多翻译 {MaxTextLength:N0} 个字符，请缩小选区后重试。");
        TranslationKeyStore.Validate(apiKey);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = "qwen-mt-flash",
            messages = new[] { new { role = "user", content = text } },
            translation_options = new { source_lang = "English", target_lang = "Chinese" },
            stream = false
        }), Encoding.UTF8, "application/json");

        using var response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // 不显示服务端原文，避免错误响应回显密钥或待翻译内容。
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "认证失败，请检查北京地域的百炼按量付费 API Key 和模型权限。",
                HttpStatusCode.TooManyRequests => "请求过于频繁或额度不足，请检查百炼控制台后重试。",
                HttpStatusCode.BadRequest => "服务拒绝了请求，请检查模型是否已开通，或缩短文本后重试。",
                _ => $"翻译服务暂不可用（HTTP {(int)response.StatusCode}），请稍后重试。"
            });
        }

        string? result;
        string? finishReason;
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var choice = json.RootElement.GetProperty("choices")[0];
            finishReason = choice.GetProperty("finish_reason").GetString();
            result = choice.GetProperty("message").GetProperty("content").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            throw new InvalidOperationException("无法读取翻译服务的响应，请稍后重试。");
        }
        if (finishReason != "stop")
            throw new InvalidOperationException("服务未返回完整译文，可能触及长度或内容限制，请缩短文本后重试。");
        if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("服务返回了空译文，请重试。");
        return result;
    }
}
