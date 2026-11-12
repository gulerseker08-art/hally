using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hally.Infrastructure.Ai;

public record ChatTool(string Name, string Description, JsonObject Parameters);

public record ChatToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>One message in the model conversation. Role is system, user, assistant or tool.</summary>
public record ChatTurn(string Role, string? Content, IReadOnlyList<ChatToolCall>? ToolCalls = null, string? ToolCallId = null);

public record ChatCompletion(string? Content, IReadOnlyList<ChatToolCall> ToolCalls, int PromptTokens, int CompletionTokens);

/// <summary>Chat model with tool calling. The model only reads through tools and suggests; it never writes.</summary>
public interface IChatModel
{
    Task<ChatCompletion> CompleteAsync(IReadOnlyList<ChatTurn> messages, IReadOnlyList<ChatTool> tools, CancellationToken ct);
}

/// <summary>OpenAI-compatible /chat/completions with tools. Uses <c>Ai:ChatModel</c>, or the vision model when unset.</summary>
public class OpenAiCompatibleChatModel(HttpClient http, IOptions<AiOptions> options, ILogger<OpenAiCompatibleChatModel> logger) : IChatModel
{
    public async Task<ChatCompletion> CompleteAsync(IReadOnlyList<ChatTurn> messages, IReadOnlyList<ChatTool> tools, CancellationToken ct)
    {
        var o = options.Value;
        var model = string.IsNullOrWhiteSpace(o.ChatModel) ? o.VisionModel : o.ChatModel;
        if (string.IsNullOrWhiteSpace(o.BaseUrl) || string.IsNullOrWhiteSpace(o.ApiKey) || string.IsNullOrWhiteSpace(model))
        {
            throw new AiUnavailableException("AI provider is not configured (Ai:BaseUrl, Ai:ApiKey, Ai:ChatModel).");
        }

        var body = new JsonObject
        {
            ["model"] = model,
            ["temperature"] = 0.2,
            ["messages"] = new JsonArray(messages.Select(ToJson).ToArray()),
        };
        if (tools.Count > 0)
        {
            body["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = t.Parameters.DeepClone() },
            }).ToArray());
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/chat/completions") { Content = JsonContent.Create(body) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSeconds));
            using var response = await http.SendAsync(message, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AI chat provider returned {Status}", (int)response.StatusCode);
                throw new AiUnavailableException($"AI provider returned {(int)response.StatusCode}.");
            }

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
            var msg = json.RootElement.GetProperty("choices")[0].GetProperty("message");
            var calls = new List<ChatToolCall>();
            if (msg.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in tc.EnumerateArray())
                {
                    var fn = call.GetProperty("function");
                    calls.Add(new ChatToolCall(call.GetProperty("id").GetString()!, fn.GetProperty("name").GetString()!, fn.GetProperty("arguments").GetString() ?? "{}"));
                }
            }

            var usage = json.RootElement.TryGetProperty("usage", out var u) ? u : default;
            int Tokens(string name) => usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var v) ? v.GetInt32() : 0;
            var content = msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            return new ChatCompletion(content, calls, Tokens("prompt_tokens"), Tokens("completion_tokens"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new AiUnavailableException("AI chat call failed.", ex);
        }
    }

    private static JsonNode ToJson(ChatTurn t)
    {
        var node = new JsonObject { ["role"] = t.Role, ["content"] = t.Content };
        if (t.ToolCalls is { Count: > 0 } calls)
        {
            node["tool_calls"] = new JsonArray(calls.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.Id,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.ArgumentsJson },
            }).ToArray());
        }

        if (t.ToolCallId is not null)
        {
            node["tool_call_id"] = t.ToolCallId;
        }

        return node;
    }
}
