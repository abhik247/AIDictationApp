using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIDictationApp.Models;

namespace AIDictationApp.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public SettingsService()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "AIDictationApp");

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            _settingsPath = Path.Combine(folder, "settings.json");
        }

        public AppSettings Load()
        {
            if (!File.Exists(_settingsPath))
            {
                var newSettings = new AppSettings();
                PopulateDefaultPrompts(newSettings);
                return newSettings;
            }

            AppSettings settings;
            try
            {
                var json = File.ReadAllText(_settingsPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                settings = new AppSettings();
            }

            if (settings.RewordingPrompts == null || settings.RewordingPrompts.Count == 0)
            {
                PopulateDefaultPrompts(settings);
            }

            // Migrate old setting if needed
            if (string.IsNullOrEmpty(settings.TranscriptionApiKey) && !string.IsNullOrEmpty(settings.OpenAIApiKey))
            {
                settings.TranscriptionApiKey = settings.OpenAIApiKey;
                settings.RewordingApiKey = settings.OpenAIApiKey;
            }

            return settings;
        }

        public async Task SaveAsync(AppSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await File.WriteAllTextAsync(_settingsPath, json).ConfigureAwait(false);
            }
            catch
            {
                // Swallow persistence failures to avoid breaking the UI flow.
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private void PopulateDefaultPrompts(AppSettings settings)
        {
            settings.RewordingPrompts = new System.Collections.Generic.List<RewordingPrompt>
            {
                new RewordingPrompt { Name = "Rephrase", Instructions = "Rephrase this text cleanly.", Order = 0 },
                new RewordingPrompt { Name = "Shorten", Instructions = "Make this text shorter and more concise.", Order = 1 },
                new RewordingPrompt { Name = "Friendly", Instructions = "Rewrite this text in a friendly tone.", Order = 2 },
                new RewordingPrompt { Name = "Formal", Instructions = "Rewrite this text in a formal tone.", Order = 3 },
                new RewordingPrompt { Name = "Engaging", Instructions = "Make this text more engaging and captivating.", Order = 4 },
                new RewordingPrompt { Name = "Casual", Instructions = "Rewrite this text in a casual tone.", Order = 5 },
                new RewordingPrompt { Name = "Professional", Instructions = "Make this text professional and business-appropriate.", Order = 6 },
                new RewordingPrompt { Name = "Diplomatic", Instructions = "Rewrite this text to be polite and diplomatic.", Order = 7 },
                new RewordingPrompt { Name = "Exciting", Instructions = "Rewrite this text to be energetic and exciting.", Order = 8 },
                new RewordingPrompt { Name = "Detailed", Instructions = "Expand on this text and add illustrative details.", Order = 9 }
            };
        }
    }
}