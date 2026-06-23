using AIDictationApp.Helpers;
using AIDictationApp.Models;
using AIDictationApp.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Dispatching;

namespace AIDictationApp.ViewModels
{
    public enum RecordingState
    {
        Idle,
        Recording,
        Recorded
    }
    public class MainViewModel : BaseViewModel
    {
        private static readonly System.Collections.Generic.IReadOnlyList<string> s_availableLanguages =
            new[] { "English", "Bengali (bn-IN)", "Hindi" };

        private readonly AudioRecorder _recorder;
        private readonly OpenAIService _openAI;
        private readonly RewordService _rewordService;
        private readonly SettingsService _settingsService;
        private readonly StartupRegistrationService _startupRegistrationService;
        
        private AppSettings _settings;
        private readonly DispatcherQueue _dispatcherQueue;

        private string _transcript = "";

        // Undo / Redo fields
        private readonly System.Collections.Generic.List<string> _undoStack = new() { "" };
        private int _undoIndex = 0;
        private bool _isUpdatingFromUndoRedo = false;
        private DispatcherQueueTimer? _debounceTimer;

        private void PushToUndoStack(string value)
        {
            if (_undoStack.Count > 0 && _undoStack[_undoIndex] == value) return;

            if (_undoIndex < _undoStack.Count - 1)
            {
                _undoStack.RemoveRange(_undoIndex + 1, _undoStack.Count - _undoIndex - 1);
            }

            _undoStack.Add(value);
            if (_undoStack.Count > 101) // 100 actions limit
            {
                _undoStack.RemoveAt(0);
                _undoIndex--;
            }
            _undoIndex++;

            ((RelayCommand)UndoCommand)?.RaiseCanExecuteChanged();
            ((RelayCommand)RedoCommand)?.RaiseCanExecuteChanged();
        }

        public string Transcript
        {
            get => _transcript;
            set
            {
                if (_transcript != value)
                {
                    if (!_isUpdatingFromUndoRedo)
                    {
                        if (_debounceTimer == null)
                        {
                            _debounceTimer = _dispatcherQueue.CreateTimer();
                            _debounceTimer.Interval = TimeSpan.FromMilliseconds(400);
                            _debounceTimer.Tick += (s, e) =>
                            {
                                _debounceTimer.Stop();
                                PushToUndoStack(_transcript);
                            };
                        }

                        _debounceTimer.Stop();
                        _debounceTimer.Start();
                    }
                    SetProperty(ref _transcript, value);
                }
            }
        }

        private bool _keepRunningInBackground;
        public bool KeepRunningInBackground
        {
            get => _keepRunningInBackground;
            set
            {
                if (_keepRunningInBackground == value)
                    return;

                SetProperty(ref _keepRunningInBackground, value);
                _settings.KeepRunningInBackground = value;
                _ = _settingsService.SaveAsync(_settings);
            }
        }

        private bool _alwaysOnTop;
        public bool AlwaysOnTop
        {
            get => _alwaysOnTop;
            set
            {
                if (_alwaysOnTop != value)
                {
                    SetProperty(ref _alwaysOnTop, value);
                    _settings.AlwaysOnTop = value;
                    _ = _settingsService.SaveAsync(_settings);
                    SetAlwaysOnTopAction?.Invoke(value);
                }
            }
        }

        private bool _launchAtStartup;
        public bool LaunchAtStartup
        {
            get => _launchAtStartup;
            set
            {
                if (_launchAtStartup == value)
                    return;

                var updated = _startupRegistrationService.SetEnabled(value);
                if (!updated)
                {
                    ShowMessageAction?.Invoke("Startup setting failed", "Could not update launch-at-startup setting on this device.");
                    return;
                }

                SetProperty(ref _launchAtStartup, value);
                _settings.LaunchAtStartup = value;
                _ = _settingsService.SaveAsync(_settings);
            }
        }

        public Action<string>? OnTranscribedTextReceived { get; set; }

        private double _audioLevel;
        public double AudioLevel
        {
            get => _audioLevel;
            set => SetProperty(ref _audioLevel, value);
        }

        private RecordingState _state = RecordingState.Idle;

        public RecordingState State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }

        private string _recordingTime = "00:00";
        public string RecordingTime
        {
            get => _recordingTime;
            set => SetProperty(ref _recordingTime, value);
        }

        private bool _isPaused = false;
        public bool IsPaused
        {
            get => _isPaused;
            set => SetProperty(ref _isPaused, value);
        }

        private string _transcriptionApiKey = "";
        public string TranscriptionApiKey
        {
            get => _transcriptionApiKey;
            set { SetProperty(ref _transcriptionApiKey, value); _settings.TranscriptionApiKey = value; _ = _settingsService.SaveAsync(_settings); }
        }

        private string _transcriptionHost = "";
        public string TranscriptionHost
        {
            get => _transcriptionHost;
            set { SetProperty(ref _transcriptionHost, value); _settings.TranscriptionHost = value; _ = _settingsService.SaveAsync(_settings); }
        }

        private string _transcriptionModel = "";
        public string TranscriptionModel
        {
            get => _transcriptionModel;
            set { SetProperty(ref _transcriptionModel, value); _settings.TranscriptionModel = value; _ = _settingsService.SaveAsync(_settings); }
        }

        private string _rewordingApiKey = "";
        public string RewordingApiKey
        {
            get => _rewordingApiKey;
            set { SetProperty(ref _rewordingApiKey, value); _settings.RewordingApiKey = value; _ = _settingsService.SaveAsync(_settings); }
        }

        private string _rewordingHost = "";
        public string RewordingHost
        {
            get => _rewordingHost;
            set { SetProperty(ref _rewordingHost, value); _settings.RewordingHost = value; _ = _settingsService.SaveAsync(_settings); }
        }

        public ObservableCollection<RewordingPrompt> RewordingPrompts { get; }

        private string _rewordingModel = "";
        public string RewordingModel
        {
            get => _rewordingModel;
            set { SetProperty(ref _rewordingModel, value); _settings.RewordingModel = value; _ = _settingsService.SaveAsync(_settings); }
        }
        // --- ADD THESE NEW PROPERTIES ---

        public ApiProvider[] AvailableProviders { get; } = new[] { ApiProvider.OpenAI, ApiProvider.Gemini };

        private ApiProvider _transcriptionProvider;
        public ApiProvider TranscriptionProvider
        {
            get => _transcriptionProvider;
            set
            {
                if (_transcriptionProvider != value)
                {
                    SetProperty(ref _transcriptionProvider, value);
                    _settings.TranscriptionProvider = value;
                    _settings.SelectedProvider = value;
                    _ = _settingsService.SaveAsync(_settings);
                    OnPropertyChanged(nameof(IsTranscriptionOpenAI));
                    OnPropertyChanged(nameof(IsTranscriptionGemini));
                }
            }
        }

        public bool IsTranscriptionOpenAI => _transcriptionProvider == ApiProvider.OpenAI;
        public bool IsTranscriptionGemini => _transcriptionProvider == ApiProvider.Gemini;

        private ApiProvider _rewordingProvider;
        public ApiProvider RewordingProvider
        {
            get => _rewordingProvider;
            set
            {
                if (_rewordingProvider != value)
                {
                    SetProperty(ref _rewordingProvider, value);
                    _settings.RewordingProvider = value;
                    _settings.SelectedProvider = value;
                    _ = _settingsService.SaveAsync(_settings);
                    OnPropertyChanged(nameof(IsRewordingOpenAI));
                    OnPropertyChanged(nameof(IsRewordingGemini));
                }
            }
        }

        public bool IsRewordingOpenAI => _rewordingProvider == ApiProvider.OpenAI;
        public bool IsRewordingGemini => _rewordingProvider == ApiProvider.Gemini;

        private string _geminiApiKey = "";
        public string GeminiApiKey
        {
            get => _geminiApiKey;
            set { SetProperty(ref _geminiApiKey, value); _settings.GeminiApiKey = value; _ = _settingsService.SaveAsync(_settings); }
        }

        private string _geminiHost = "";
        public string GeminiHost
        {
            get => _geminiHost;
            set { SetProperty(ref _geminiHost, value); _settings.GeminiHost = value; _ = _settingsService.SaveAsync(_settings); }
        }

        private string _geminiTranscriptionModel = "";
        public string GeminiTranscriptionModel
        {
            get => _geminiTranscriptionModel;
            set { SetProperty(ref _geminiTranscriptionModel, value); _settings.GeminiTranscriptionModel = value; _ = _settingsService.SaveAsync(_settings); }
        }

        private string _geminiRewordingModel = "";
        public string GeminiRewordingModel
        {
            get => _geminiRewordingModel;
            set { SetProperty(ref _geminiRewordingModel, value); _settings.GeminiRewordingModel = value; _ = _settingsService.SaveAsync(_settings); }
        }
        public System.Collections.Generic.IReadOnlyList<string> AvailableLanguages => s_availableLanguages;

        private string _inputLanguage = "English";
        public string InputLanguage
        {
            get => _inputLanguage;
            set
            {
                if (_inputLanguage != value)
                {
                    SetProperty(ref _inputLanguage, value);
                    _settings.InputLanguage = value;
                    _ = _settingsService.SaveAsync(_settings);
                    OnPropertyChanged(nameof(RecordButtonText));
                }
            }
        }

        public string RecordButtonText
        {
            get
            {
                return _inputLanguage switch
                {
                    "Bengali (bn-IN)" => "🎤 রেকর্ড",
                    "Hindi" => "🎤 रिकॉर्ड",
                    _ => "🎤 Record"
                };
            }
        }

        // Left for backward compatibility if it's referenced anywhere else
        private string _apiKey = "";

        public string ApiKey
        {
            get => _apiKey;
            set
            {
                SetProperty(ref _apiKey, value);
            }
        }


        public ICommand RecordCommand { get; }
        public ICommand SendCommand { get; }
        public ICommand SendAndInsertCommand { get; }
        public ICommand TrashCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand RewordCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }

        public ICommand AddPromptCommand { get; }
        public ICommand RemovePromptCommand { get; }
        public ICommand MovePromptUpCommand { get; }
        public ICommand MovePromptDownCommand { get; }
        public ICommand ApplyRewordingPromptCommand { get; }
        public ICommand ManualSaveCommand { get; }
        public ICommand InsertCommand { get; }

        public Func<string>? GetSelectedTextAction { get; set; }
        public Action<string>? ReplaceSelectedTextAction { get; set; }
        public Action? SelectAllAction { get; set; }
        public Action? HideWindowAction { get; set; }
        public Action<bool>? SetAlwaysOnTopAction { get; set; }
        public Action<string, string>? ShowMessageAction { get; set; }

        private double _smoothedLevel = 0;
        public MainViewModel()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            _settingsService = new SettingsService();
            _startupRegistrationService = new StartupRegistrationService();
            _settings = _settingsService.Load();
            _apiKey = _settings.OpenAIApiKey;

            _transcriptionApiKey = _settings.TranscriptionApiKey ?? "";
            _transcriptionHost = _settings.TranscriptionHost ?? "https://api.openai.com/v1";
            _transcriptionModel = _settings.TranscriptionModel ?? "whisper-1";

            _rewordingApiKey = _settings.RewordingApiKey ?? "";
            _rewordingHost = _settings.RewordingHost ?? "https://api.openai.com/v1";
            _rewordingModel = _settings.RewordingModel ?? "gpt-4o-mini";
            
            // --- ADD INITIALIZATION FOR GEMINI ---
            _transcriptionProvider = _settings.TranscriptionProvider;
            _rewordingProvider = _settings.RewordingProvider;
            _geminiApiKey = _settings.GeminiApiKey ?? "";
            _geminiHost = _settings.GeminiHost ?? "https://generativelanguage.googleapis.com/v1beta/openai/";
            _geminiTranscriptionModel = _settings.GeminiTranscriptionModel ?? "gemini-1.5-flash";
            _geminiRewordingModel = _settings.GeminiRewordingModel ?? "gemini-1.5-flash";
            
            RewordingPrompts = new ObservableCollection<RewordingPrompt>(_settings.RewordingPrompts);
            bool foundActive = false;
            foreach (var prompt in RewordingPrompts)
            {
                if (prompt.ApplyAutomatically)
                {
                    if (foundActive) prompt.ApplyAutomatically = false;
                    else foundActive = true;
                }
            }
            RewordingPrompts.CollectionChanged += (s, e) => SavePrompts();

            _inputLanguage = _settings.InputLanguage ?? "English";
            _alwaysOnTop = _settings.AlwaysOnTop;

            if (_settings.LaunchAtStartup)
            {
                _startupRegistrationService.SetEnabled(true);
            }

            _launchAtStartup = _startupRegistrationService.IsEnabled();
            if (_settings.LaunchAtStartup != _launchAtStartup)
            {
                _settings.LaunchAtStartup = _launchAtStartup;
                _ = _settingsService.SaveAsync(_settings);
            }

            _keepRunningInBackground = _settings.KeepRunningInBackground;

            _recorder = new AudioRecorder();
            _recorder.AudioLevelChanged += level =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    double target = level * 100;

                    // Smooth transition (low-pass filter)
                    _smoothedLevel = (_smoothedLevel * 0.7) + (target * 0.3);

                    AudioLevel = _smoothedLevel;
                });
            };

            _recorder.RecordingDurationChanged += ts =>
            {
                var s = $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}";
                if (App.MainAppWindow?.DispatcherQueue != null)
                {
                    App.MainAppWindow.DispatcherQueue.TryEnqueue(() =>
                    {
                        RecordingTime = s;
                    });
                }
                else
                {
                    RecordingTime = s;
                }
            };

            _openAI = new OpenAIService();
            _rewordService = new RewordService();

            RecordCommand = new RelayCommand(HandleRecord);
            SendCommand = new RelayCommand(async () => await SendForTranscriptionAsync(false));
            SendAndInsertCommand = new RelayCommand(async () => await SendForTranscriptionAsync(true));
            TrashCommand = new RelayCommand(DeleteRecording);
            PauseCommand = new RelayCommand(TogglePause);
            RewordCommand = new RelayCommand(RewordText);
            CopyCommand = new RelayCommand(CopyText);
            InsertCommand = new RelayCommand(InsertText);
            SelectAllCommand = new RelayCommand(() => SelectAllAction?.Invoke());
            ClearAllCommand = new RelayCommand(() => Transcript = "");

            UndoCommand = new RelayCommand(Undo, CanUndo);
            RedoCommand = new RelayCommand(Redo, CanRedo);

            AddPromptCommand = new RelayCommand(AddPrompt);
            RemovePromptCommand = new RelayCommand<RewordingPrompt>(RemovePrompt);
            MovePromptUpCommand = new RelayCommand<RewordingPrompt>(MovePromptUp);
            MovePromptDownCommand = new RelayCommand<RewordingPrompt>(MovePromptDown);
            ApplyRewordingPromptCommand = new RelayCommand<RewordingPrompt>(ApplyRewordingPrompt);
            ManualSaveCommand = new RelayCommand(SavePrompts);

            foreach (var prompt in RewordingPrompts)
            {
                InitializePromptCommands(prompt);
            }
        }

        private void InitializePromptCommands(RewordingPrompt prompt)
        {
            prompt.ApplyCommand = new RelayCommand(() => ApplyRewordingPromptCommand.Execute(prompt));
            prompt.RemoveCommand = new RelayCommand(() => RemovePromptCommand.Execute(prompt));
            prompt.MoveUpCommand = new RelayCommand(() => MovePromptUpCommand.Execute(prompt));
            prompt.MoveDownCommand = new RelayCommand(() => MovePromptDownCommand.Execute(prompt));
            prompt.ValidateApplyAutomatically = HandleValidateAutoApply;

            prompt.PropertyChanged -= Prompt_PropertyChanged;
            prompt.PropertyChanged += Prompt_PropertyChanged;
        }

        private void Prompt_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RewordingPrompt.ApplyAutomatically) ||
                e.PropertyName == nameof(RewordingPrompt.Name) ||
                e.PropertyName == nameof(RewordingPrompt.Instructions))
            {
                SavePrompts();
            }
        }

        private bool HandleValidateAutoApply(RewordingPrompt prompt)
        {
            var activePrompt = RewordingPrompts.FirstOrDefault(p => p.ApplyAutomatically && p != prompt);
            if (activePrompt != null)
            {
                if (ShowMessageAction != null)
                {
                    App.MainAppWindow?.DispatcherQueue.TryEnqueue(() =>
                    {
                        ShowMessageAction?.Invoke(
                            "Cannot apply automatically",
                            $"Only one prompt can be set to \"Apply Automatically\" at a time.\n\nCurrently, \"{activePrompt.Name}\" is enabled. Please turn it off before activating this prompt."
                        );
                    });
                }
                return false;
            }
            return true;
        }

        private void AddPrompt()
        {
            var p = new RewordingPrompt { Name = "New Prompt", Instructions = "Instructions here", Order = RewordingPrompts.Count, ApplyAutomatically = false };
            InitializePromptCommands(p);
            RewordingPrompts.Add(p);
            SavePrompts();
        }

        private void RemovePrompt(RewordingPrompt prompt)
        {
            if (prompt != null)
            {
                prompt.PropertyChanged -= Prompt_PropertyChanged;
                RewordingPrompts.Remove(prompt);
                SavePrompts();
            }
        }

        private void MovePromptUp(RewordingPrompt prompt)
        {
            if (prompt == null) return;
            var index = RewordingPrompts.IndexOf(prompt);
            if (index > 0)
            {
                RewordingPrompts.Move(index, index - 1);
                SavePrompts();
            }
        }

        private void MovePromptDown(RewordingPrompt prompt)
        {
            if (prompt == null) return;
            var index = RewordingPrompts.IndexOf(prompt);
            if (index >= 0 && index < RewordingPrompts.Count - 1)
            {
                RewordingPrompts.Move(index, index + 1);
                SavePrompts();
            }
        }

        private async void ApplyRewordingPrompt(RewordingPrompt prompt)
        {
            if (string.IsNullOrWhiteSpace(Transcript) || prompt == null)
                return;

            string? selectedText = GetSelectedTextAction?.Invoke();
            bool hasSelection = !string.IsNullOrEmpty(selectedText);
            var original = hasSelection ? selectedText : Transcript;

            if (string.IsNullOrWhiteSpace(original))
                return;

            try
            {
                var (apiKey, host, model) = GetRewordingConfig();
                var improved = await _rewordService.RewordAsync(original, apiKey, host, model, _inputLanguage, prompt.Instructions);

                if (hasSelection)
                {
                    ReplaceSelectedTextAction?.Invoke(improved);
                }
                else
                {
                    Transcript = improved;
                }
            }
            catch (Exception ex)
            {
                // Leave original text untouched
            }
        }

        private bool CanUndo() => _undoIndex > 0;

        private void SavePrompts()
        {
            _settings.RewordingPrompts = new System.Collections.Generic.List<RewordingPrompt>(RewordingPrompts);
            for (int i = 0; i < _settings.RewordingPrompts.Count; i++)
            {
                _settings.RewordingPrompts[i].Order = i;
            }
            _ = _settingsService.SaveAsync(_settings);
        }

        private void Undo()
        {
            if (_debounceTimer != null && _debounceTimer.IsRunning)
            {
                _debounceTimer.Stop();
                PushToUndoStack(_transcript);
            }

            if (CanUndo())
            {
                _isUpdatingFromUndoRedo = true;
                _undoIndex--;
                Transcript = _undoStack[_undoIndex];
                _isUpdatingFromUndoRedo = false;

                ((RelayCommand)UndoCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RedoCommand).RaiseCanExecuteChanged();
            }
        }

        private bool CanRedo() => _undoIndex < _undoStack.Count - 1;
        private void Redo()
        {
            if (_debounceTimer != null && _debounceTimer.IsRunning)
            {
                _debounceTimer.Stop();
                PushToUndoStack(_transcript);
            }

            if (CanRedo())
            {
                _isUpdatingFromUndoRedo = true;
                _undoIndex++;
                Transcript = _undoStack[_undoIndex];
                _isUpdatingFromUndoRedo = false;

                ((RelayCommand)UndoCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RedoCommand).RaiseCanExecuteChanged();
            }
        }

        private void HandleRecord()
        {
            if (State == RecordingState.Idle)
            {
                _recorder.StartRecording();
                IsPaused = false;
                State = RecordingState.Recording;
            }
            // do not treat Record as stop — user must press Send to send or Discard to throw away
        }

        private void TogglePause()
        {
            if (State != RecordingState.Recording) return;

            _recorder.PauseRecording();

            if (_recorder.IsPaused)
            {
                IsPaused = true;
            }
            else
            {
                IsPaused = false;
            }
        }

        private string GetLanguageCode(string language)
        {
            return language switch
            {
                "Bengali (bn-IN)" => "bn",
                "Hindi" => "hi",
                _ => "en"
            };
        }

        private (string ApiKey, string Host, string Model) GetTranscriptionConfig()
        {
            return _transcriptionProvider == ApiProvider.Gemini
                ? (_settings.GeminiApiKey, _settings.GeminiHost, _settings.GeminiTranscriptionModel)
                : (_settings.TranscriptionApiKey, _settings.TranscriptionHost, _settings.TranscriptionModel);
        }

        private (string ApiKey, string Host, string Model) GetRewordingConfig()
        {
            return _rewordingProvider == ApiProvider.Gemini
                ? (_settings.GeminiApiKey, _settings.GeminiHost, _settings.GeminiRewordingModel)
                : (_settings.RewordingApiKey, _settings.RewordingHost, _settings.RewordingModel);
        }

        private async Task SendForTranscriptionAsync(bool insertIntoActiveApp)
        {
            if (State != RecordingState.Recording) return;

            IsPaused = false;
            // Stop recording and finalize file
            _recorder.StopRecording();

            // Give small delay to ensure file flushed
            await Task.Delay(300);

            var path = _recorder.OutputFilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                OnTranscribedTextReceived?.Invoke("\n[Error: audio file not found.]\n");
                State = RecordingState.Idle;
                return;
            }

            State = RecordingState.Recorded; // show sent state in UI while awaiting
            try
            {
                var langCode = GetLanguageCode(_inputLanguage);
                var (transApiKey, transHost, transModel) = GetTranscriptionConfig();
                var (rewordApiKey, rewordHost, rewordModel) = GetRewordingConfig();

                var text = await _openAI.TranscribeAudioAsync(path, transApiKey, transHost, transModel, langCode);

                foreach (var prompt in RewordingPrompts)
                {
                    if (prompt.ApplyAutomatically)
                    {
                        try
                        {
                            // Use the dynamically selected rewording variables
                            text = await _rewordService.RewordAsync(text, rewordApiKey, rewordHost, rewordModel, _inputLanguage, prompt.Instructions);
                        }
                        catch (Exception ex)
                        {
                            text += $"\n[Error applying prompt '{prompt.Name}': {ex.Message}]\n";
                        }
                    }
                }
                // ... rest of the method remains unchanged ...

                OnTranscribedTextReceived?.Invoke(text);

                if (insertIntoActiveApp && !string.IsNullOrWhiteSpace(text))
                {
                    await InsertTextAsync(text, true);
                }
            }
            catch (Exception ex)
            {
                // We could show error via the event too, but simple fallback is just to insert it.
                OnTranscribedTextReceived?.Invoke($"\n[Error transcribing: {ex.Message}]\n");
            }
            finally
            {
                // after send, set back to idle
                State = RecordingState.Idle;
                // optionally delete the temp file after sending
                try { File.Delete(path); } catch { }
            }
        }

        private void DeleteRecording()
        {
            // stop if recording
            if (_recorder.IsRecording)
            {
                _recorder.StopRecording();
            }

            // delete temp file
            _recorder.DeleteFile();

            IsPaused = false;
            State = RecordingState.Idle;
            RecordingTime = "00:00";
        }

        

        private async void RewordText()
        {
            if (string.IsNullOrWhiteSpace(Transcript))
                return;

            string? selectedText = GetSelectedTextAction?.Invoke();
            bool hasSelection = !string.IsNullOrEmpty(selectedText);

            var original = hasSelection ? selectedText : Transcript;

            if (string.IsNullOrWhiteSpace(original))
                return;

            try
            {
                var (apiKey, host, model) = GetRewordingConfig();
                var improved = await _rewordService.RewordAsync(original, apiKey, host, model, _inputLanguage, "");

                if (hasSelection)
                {
                    ReplaceSelectedTextAction?.Invoke(improved);
                }
                else
                {
                    Transcript = improved;
                }
            }
            catch (Exception ex)
            {
                // Leave original text untouched, just notify via event or append
                OnTranscribedTextReceived?.Invoke($"\n[Rewrite Error: {ex.Message}]\n");
            }
        }

        private void CopyText()
        {
            string? selectedText = GetSelectedTextAction?.Invoke();
            string textToCopy = string.IsNullOrEmpty(selectedText) ? Transcript : selectedText;

            if (string.IsNullOrWhiteSpace(textToCopy))
                return;

            var package = new DataPackage();
            package.SetText(textToCopy);

            Clipboard.SetContent(package);

            // Removing inline message to prevent text box pollution.
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private async void InsertText()
        {
            string? selectedText = GetSelectedTextAction?.Invoke();
            string textToInsert = string.IsNullOrEmpty(selectedText) ? Transcript : selectedText;

            if (string.IsNullOrWhiteSpace(textToInsert))
                return;

            await InsertTextAsync(textToInsert, true);
        }

        private async Task InsertTextAsync(string textToInsert, bool hideWindowFirst)
        {
            if (string.IsNullOrWhiteSpace(textToInsert))
                return;

            var package = new DataPackage();
            package.SetText(textToInsert);
            Clipboard.SetContent(package);
            Clipboard.Flush(); // Flush to clipboard so it is ready immediately

            if (hideWindowFirst)
            {
                // Hide the window so the previous application regains focus
                HideWindowAction?.Invoke();
            }

            // Give the OS time to restore focus to the underlying application
            await Task.Delay(500);

            // Simulate Ctrl+V
            const int KEYEVENTF_KEYUP = 0x0002;
            const byte VK_CONTROL = 0x11;
            const byte VK_V = 0x56;

            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }
}