using Microsoft.Win32;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;
using System.Windows.Data;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace JARVIS
{
    public class VoiceCommand
    {
        public string Phrase { get; set; }
        public string Path { get; set; }
    }


    public interface IAudioRecorder
    {
        bool IsRecording { get; }
        void StartRecording(string outputFilePath);
        void StopRecording();
    }

    public class AudioRecorder : IAudioRecorder, IDisposable
    {
        private WaveInEvent _waveIn;
        private WaveFileWriter _writer;
        public bool IsRecording { get; private set; }

        public void StartRecording(string outputFilePath)
        {
            if (IsRecording) return;

            try
            {
                _waveIn = new WaveInEvent
                {
                    DeviceNumber = 0, 
                    WaveFormat = new WaveFormat(16000, 16, 1)
                };
                _writer = new WaveFileWriter(outputFilePath, _waveIn.WaveFormat);
                _waveIn.DataAvailable += (s, e) => _writer?.Write(e.Buffer, 0, e.BytesRecorded);
                _waveIn.StartRecording();
                IsRecording = true;
            }
            catch (Exception ex) when (ex.Message.Contains("BadDeviceId") || ex is NAudio.MmException)
            {
                _waveIn?.Dispose();
                _waveIn = null;
                _writer?.Dispose();
                _writer = null;
                throw new InvalidOperationException("Устройство ввода звука не найдено. Проверьте микрофон.", ex);
            }
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveIn = null;
            _writer?.Dispose();
            _writer = null;
            IsRecording = false;
        }

        public void Dispose() => StopRecording();
    }

    public interface IWhisperTranscriber
    {
        Task<string> TranscribeAsync(string audioFilePath);
    }

    public class WhisperTranscriber : IWhisperTranscriber
    {
        private readonly string _modelPath;
        public WhisperTranscriber(string modelPath = null)
        {
            _modelPath = modelPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ggml-small.bin");
        }

        public async Task<string> TranscribeAsync(string audioFilePath)
        {
            if (!File.Exists(_modelPath)) throw new FileNotFoundException("Модель Whisper не найдена", _modelPath);
            if (!File.Exists(audioFilePath)) throw new FileNotFoundException("Аудиофайл не найден", audioFilePath);

            RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary> 
            { 
                RuntimeLibrary.Cpu,
                RuntimeLibrary.CpuNoAvx

            };
            using var factory = WhisperFactory.FromPath(_modelPath);
            using var processor = factory.CreateBuilder().WithLanguage("ru").Build();

            var result = new StringBuilder();
            await using var fileStream = File.OpenRead(audioFilePath);
            await foreach (var segment in processor.ProcessAsync(fileStream))
                result.Append(segment.Text);
            return result.ToString();
        }
    }

    public interface IOllamaService
    {
        Task<string> GenerateResponseAsync(string prompt);
    }

    public class OllamaService : IOllamaService
    {
        private readonly HttpClient _client;
        private readonly string _baseUrl;
        private readonly string _model;
        public OllamaService(string baseUrl = "http://localhost:11434", string model = "phi3:mini")
        {
            _baseUrl = baseUrl;
            _model = model;
            _client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        public async Task<string> GenerateResponseAsync(string prompt)
        {
            var request = new { model = _model, prompt, stream = false };
            var response = await _client.PostAsJsonAsync($"{_baseUrl}/api/generate", request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("response").GetString();
        }
    }

    public interface ISpeechSynthesizer
    {
        void Speak(string text);
        Task SpeakAsync(string text);
        void Stop(); 
    }

    public class SpeechSynthesizerService : ISpeechSynthesizer
    {
        private readonly SpeechSynthesizer _synth = new SpeechSynthesizer();

        public void Speak(string text)
        {
            if (!string.IsNullOrEmpty(text))
                _synth.Speak(text);
        }

        public Task SpeakAsync(string text)
        {
            return Task.Run(() =>
            {
                try
                {
                    Speak(text);
                }
                catch (OperationCanceledException)
                {
                }
            });
        }

        public void Stop()
        {
            _synth.SpeakAsyncCancelAll(); // прерывает текущий синтез
        }
    }

    public interface IProcessLauncher
    {
        void LaunchFile(string filePath);
        void LaunchUrl(string url);
    }

    public class ProcessLauncher : IProcessLauncher
    {
        public void LaunchFile(string filePath) => Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        public void LaunchUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public interface ICommandsRepository
    {
        Task<List<VoiceCommand>> LoadAsync();
        Task SaveAsync(List<VoiceCommand> commands);
    }

    public class CommandsRepository : ICommandsRepository
    {
        private readonly string _filePath;
        public CommandsRepository(string filePath = "commands.json") => _filePath = filePath;

        public async Task<List<VoiceCommand>> LoadAsync()
        {
            if (!File.Exists(_filePath)) return new List<VoiceCommand>();
            try
            {
                string json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<List<VoiceCommand>>(json) ?? new List<VoiceCommand>();
            }
            catch { return new List<VoiceCommand>(); }
        }

        public async Task SaveAsync(List<VoiceCommand> commands)
        {
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };
            string json = JsonSerializer.Serialize(commands, options);
            await File.WriteAllTextAsync(_filePath, json);
        }
    }

    // Преобразователи
    public class BoolToRecordingLabel : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? "SP" : "GO";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b) ? !b : false;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}