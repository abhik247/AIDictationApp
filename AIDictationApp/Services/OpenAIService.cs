using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIDictationApp.Services
{
    public class OpenAIService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string GeminiHostName = "generativelanguage.googleapis.com";

        public async Task<string> TranscribeAudioAsync(string filePath, string apiKey, string host, string model, string languageCode)
        {
            if (IsGeminiHost(host))
            {
                return await TranscribeWithGeminiAsync(filePath, apiKey, host, model).ConfigureAwait(false);
            }

            var endpoint = host.TrimEnd('/') + "/audio/transcriptions";
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var form = new MultipartFormDataContent();

            await using var fileStream = File.OpenRead(filePath);
            using var audioContent = new StreamContent(fileStream);

            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

            form.Add(audioContent, "file", "audio.wav");
            form.Add(new StringContent(model), "model");
            if (!string.IsNullOrEmpty(languageCode))
            {
                form.Add(new StringContent(languageCode), "language");
            }

            request.Content = form;

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            var text = doc.RootElement
                .GetProperty("text")
                .GetString();

            return text ?? "";
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

        private async Task<string> TranscribeWithGeminiAsync(string filePath, string apiKey, string host, string model)
        {
            var geminiBaseUrl = host.TrimEnd('/');
            if (geminiBaseUrl.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
            {
                geminiBaseUrl = geminiBaseUrl[..^"/openai".Length];
            }

            var endpoint = $"{geminiBaseUrl}/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
            var audioBytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
            var audioBase64 = Convert.ToBase64String(audioBytes);

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new
                            {
                                text = "Transcribe this audio and return only the transcribed text."
                            },
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = "audio/wav",
                                    data = audioBase64
                                }
                            }
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

            throw new InvalidOperationException("Gemini transcription response did not contain transcribed text.");
        }
    }
}