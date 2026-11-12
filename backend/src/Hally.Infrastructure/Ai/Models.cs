using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hally.Infrastructure.Ai;

public class AiOptions
{
    public const string Section = "Ai";
    /// <summary>OpenAI-compatible base URL, e.g. https://api.openai.com/v1</summary>
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string VisionModel { get; set; } = string.Empty;

    /// <summary>Model for the assistant chat (tool calling). Falls back to <see cref="VisionModel"/>.</summary>
    public string ChatModel { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(VisionModel);
}

public record ImagePart(byte[] Content, string ContentType);

/// <summary>A structured-output request: system rules, untrusted user content, optional images and a JSON schema.</summary>
public record StructuredRequest(string SystemPrompt, string UserContent, IReadOnlyList<ImagePart> Images, string SchemaName, JsonObject Schema);

public class AiUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Multimodal model that returns JSON matching a schema. Swap providers behind this interface.</summary>
public interface IVisionModel
{
    Task<JsonDocument> CompleteJsonAsync(StructuredRequest request, CancellationToken ct);
}

/// <summary>Calls any OpenAI-compatible /chat/completions endpoint with json_schema structured output.</summary>
public class OpenAiCompatibleVisionModel(HttpClient http, IOptions<AiOptions> options, ILogger<OpenAiCompatibleVisionModel> logger) : IVisionModel
{
    public async Task<JsonDocument> CompleteJsonAsync(StructuredRequest request, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsConfigured)
        {
            throw new AiUnavailableException("AI provider is not configured (Ai:BaseUrl, Ai:ApiKey, Ai:VisionModel).");
        }

        var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = request.UserContent } };
        foreach (var image in request.Images)
        {
            content.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Content)}" },
            });
        }

        var body = new JsonObject
        {
            ["model"] = o.VisionModel,
            ["temperature"] = 0,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = content },
            },
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = request.SchemaName, ["strict"] = true, ["schema"] = request.Schema.DeepClone() },
            },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(body),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSeconds));
            using var response = await http.SendAsync(message, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AI provider returned {Status}", (int)response.StatusCode);
                throw new AiUnavailableException($"AI provider returned {(int)response.StatusCode}.");
            }

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
            var text = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return JsonDocument.Parse(text ?? "{}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            throw new AiUnavailableException("AI provider call failed.", ex);
        }
    }
}
