using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIDictationApp.Services
{
    public class RewordService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string GeminiHostName = "generativelanguage.googleapis.com";

        public async Task<string> RewordAsync(string inputText, string apiKey, string host, string model, string languageName, string customInstructions = "")
        {
            if (IsGeminiHost(host))
            {
                return await RewordWithGeminiAsync(inputText, apiKey, host, model, languageName, customInstructions).ConfigureAwait(false);
            }

            var systemPrompt = string.IsNullOrWhiteSpace(customInstructions)
                ? $"Rewrite the following text to be clear, grammatically correct, and natural in {languageName}:\n\n{inputText}"
                : $"{customInstructions}\n\nPlease output the result in {languageName}:\n\n{inputText}";

            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = systemPrompt
                    }
                }
            };

            var endpoint = host.TrimEnd('/') + "/chat/completions";
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(requestBody);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            var output = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return output ?? "";
        }

        private static bool IsGeminiHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            return Uri.TryCreate(host, UriKind.Absolute, out var uri)
                && uri.Host.Contains(GeminiHostName, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string> RewordWithGeminiAsync(string inputText, string apiKey, string host, string model, string languageName, string customInstructions)
        {
            var geminiBaseUrl = host.TrimEnd('/');
            if (geminiBaseUrl.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
            {
                geminiBaseUrl = geminiBaseUrl[..^"/openai".Length];
            }

            var prompt = string.IsNullOrWhiteSpace(customInstructions)
                ? $"Rewrite the following text to be clear, grammatically correct, and natural in {languageName}. Return only the rewritten text, nothing else:\n\n{inputText}"
                : $"{customInstructions}\n\nPlease output the result in {languageName}. Return only the rewritten text, nothing else:\n\n{inputText}";

            var endpoint = $"{geminiBaseUrl}/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt }
                        }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            if (doc.RootElement.TryGetProperty("candidates", out var candidates)
                && candidates.ValueKind == JsonValueKind.Array
                && candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("content", out var contentNode)
                    && contentNode.TryGetProperty("parts", out var parts)
                    && parts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var textPart))
                        {
                            return textPart.GetString() ?? "";
                        }
                    }
                }
            }

            throw new InvalidOperationException("Gemini rewording response did not contain rewritten text.");
        }
    }
}