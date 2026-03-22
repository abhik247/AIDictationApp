using NAudio.Wave;
using System;
using System.IO;

namespace AIDictationApp.Services
{
    public class AudioRecorder
    {
        private WaveInEvent? waveSource;
        private WaveFileWriter? waveFile;

        public string OutputFilePath { get; private set; } = "";

        public bool IsRecording { get; private set; }
        public bool IsPaused { get; private set; }

        private DateTime _startTime;
        private DateTime? _pauseStarted;
        private TimeSpan _pausedAccumulated = TimeSpan.Zero;

        // Events
        public event Action<float>? AudioLevelChanged;
        public event Action<TimeSpan>? RecordingDurationChanged;

        public AudioRecorder()
        {
        }

        public void StartRecording()
        {
            if (IsRecording)
                return;

            // create new file
            OutputFilePath = Path.Combine(
                Path.GetTempPath(),
                $"recording_{Guid.NewGuid()}.wav");

            waveSource = new WaveInEvent();
            waveSource.DeviceNumber = 0;

            waveSource.WaveFormat = new WaveFormat(16000, 1); // 16kHz mono

            waveSource.DataAvailable += OnDataAvailable;
            waveSource.RecordingStopped += OnRecordingStopped;

            waveFile = new WaveFileWriter(OutputFilePath, waveSource.WaveFormat);

            _pausedAccumulated = TimeSpan.Zero;
            _pauseStarted = null;
            IsPaused = false;
            _startTime = DateTime.UtcNow;

            waveSource.StartRecording();

            IsRecording = true;
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            // write only when not paused
            if (!IsPaused)
            {
                waveFile?.Write(e.Buffer, 0, e.BytesRecorded);
                // Flush inside audio callback causes massive performance issues. NAudio handles flushing on dispose.
            }

            // compute mic level (0..1)
            float max = 0f;
            if (e.BytesRecorded > 0)
            {
                var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(e.Buffer.AsSpan(0, e.BytesRecorded));
                foreach (var sample in samples)
                {
                    float sample32 = Math.Abs(sample / 32768f);
                    if (sample32 > max) max = sample32;
                }
            }

            AudioLevelChanged?.Invoke(max);

            // compute duration excluding paused time
            var now = DateTime.UtcNow;
            var paused = _pausedAccumulated;
            if (_pauseStarted.HasValue)
                paused += now - _pauseStarted.Value;

            var duration = now - _startTime - paused;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

            RecordingDurationChanged?.Invoke(duration);
        }

        public void PauseRecording()
        {
            if (!IsRecording) return;

            if (!IsPaused)
            {
                // start pause
                _pauseStarted = DateTime.UtcNow;
                IsPaused = true;
            }
            else
            {
                // resume
                if (_pauseStarted.HasValue)
                {
                    _pausedAccumulated += DateTime.UtcNow - _pauseStarted.Value;
                    _pauseStarted = null;
                }
                IsPaused = false;
            }
        }

        public void StopRecording()
        {
            if (!IsRecording)
                return;

            // stop capturing; the RecordingStopped handler will finalize
            waveSource?.StopRecording();
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            try
            {
                // dispose wave source
                waveSource?.Dispose();
                waveSource = null;

                if (waveFile != null)
                {
                    waveFile.Dispose();
                    waveFile = null;
                }
            }
            finally
            {
                IsRecording = false;
                IsPaused = false;
                _pauseStarted = null;
                _pausedAccumulated = TimeSpan.Zero;
            }
        }

        public void DeleteFile()
        {
            try
            {
                if (!string.IsNullOrEmpty(OutputFilePath) && File.Exists(OutputFilePath))
                    File.Delete(OutputFilePath);
            }
            catch
            {
                // ignore
            }
            finally
            {
                OutputFilePath = "";
            }
        }
    }
}