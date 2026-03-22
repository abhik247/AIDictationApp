namespace AIDictationApp.Models
{
    public class AppSettings
    {
        public string TranscriptionApiKey { get; set; } = "";
        public string TranscriptionHost { get; set; } = "https://api.openai.com/v1";
        public string TranscriptionModel { get; set; } = "whisper-1";

        public string RewordingApiKey { get; set; } = "";
        public string RewordingHost { get; set; } = "https://api.openai.com/v1";
        public string RewordingModel { get; set; } = "gpt-4o-mini";

        public string InputLanguage { get; set; } = "English";

        public bool AlwaysOnTop { get; set; } = false;

        public bool LaunchAtStartup { get; set; } = false;

        public bool KeepRunningInBackground { get; set; } = false;

        public System.Collections.Generic.List<RewordingPrompt> RewordingPrompts { get; set; } = new();

        // Retained for backward compat
        public string OpenAIApiKey { get; set; } = "";
    }
}