using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIDictationApp.Services
{
    public class OpenAIService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public async Task<string> TranscribeAudioAsync(string filePath, string apiKey, string host, string model, string languageCode)
        {
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
    }
}