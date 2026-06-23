namespace AIDictationApp.Models
{
    public enum ApiProvider
    {
        OpenAI,
        Gemini
    }

    public class AppSettings
    {
        // --- Global Provider Choice ---
        public ApiProvider SelectedProvider { get; set; } = ApiProvider.OpenAI;
        public ApiProvider TranscriptionProvider { get; set; } = ApiProvider.OpenAI;
        public ApiProvider RewordingProvider { get; set; } = ApiProvider.OpenAI;

        // --- OpenAI Settings ---
        public string TranscriptionApiKey { get; set; } = "";
        public string TranscriptionHost { get; set; } = "https://api.openai.com/v1";
        public string TranscriptionModel { get; set; } = "whisper-1";

        public string RewordingApiKey { get; set; } = "";
        public string RewordingHost { get; set; } = "https://api.openai.com/v1";
        public string RewordingModel { get; set; } = "gpt-4o-mini";

        // --- Gemini Settings ---
        // Gemini's OpenAI compatibility layer base URL
        public string GeminiApiKey { get; set; } = "";
        public string GeminiHost { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";
        public string GeminiTranscriptionModel { get; set; } = "gemini-1.5-flash";
        public string GeminiRewordingModel { get; set; } = "gemini-1.5-flash";

        // --- General Settings ---
        public string InputLanguage { get; set; } = "English";
        public bool AlwaysOnTop { get; set; } = false;
        public bool LaunchAtStartup { get; set; } = false;
        public bool KeepRunningInBackground { get; set; } = false;

        public System.Collections.Generic.List<RewordingPrompt> RewordingPrompts { get; set; } = new();

        // Retained for backward compat
        public string OpenAIApiKey { get; set; } = "";
    }
}