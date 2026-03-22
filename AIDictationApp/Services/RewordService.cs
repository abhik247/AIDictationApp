using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIDictationApp.Services
{
    public class RewordService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public async Task<string> RewordAsync(string inputText, string apiKey, string host, string model, string languageName, string customInstructions = "")
        {
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
    }
}