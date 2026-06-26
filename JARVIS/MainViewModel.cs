using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;

namespace JARVIS
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IAudioRecorder _audioRecorder;
        private readonly IWhisperTranscriber _whisper;
        private readonly IOllamaService _ollama;
        private readonly ISpeechSynthesizer _speech;
        private readonly IProcessLauncher _processLauncher;
        private readonly ICommandsRepository _commandsRepo;

        private bool _microphoneAvailable = true;
        private bool _isSpeaking = false;

        private string _tempAudioFile;
        private bool _isRecording;

        public MainViewModel(
            IAudioRecorder audioRecorder,
            IWhisperTranscriber whisper,
            IOllamaService ollama,
            ISpeechSynthesizer speech,
            IProcessLauncher processLauncher,
            ICommandsRepository commandsRepo)
        {
            _audioRecorder = audioRecorder;
            _whisper = whisper;
            _ollama = ollama;
            _speech = speech;
            _processLauncher = processLauncher;
            _commandsRepo = commandsRepo;

            _tempAudioFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".wav");

            Task.Run(async () => await LoadCommandsAsync());

            StartCommand = new RelayCommand(ExecuteStart, () => !IsBusy);
            SaveCommand = new RelayCommand(ExecuteSave);
            FileBrowseCommand = new RelayCommand(ExecuteFileBrowse);
            OpenTxtCommand = new RelayCommand(ExecuteOpenTxt);
        }

        private string _status;
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

        private string _boxOllamaText;
        public string BoxOllamaText { get => _boxOllamaText; set { _boxOllamaText = value; OnPropertyChanged(); } }

        private string _boxValueText;
        public string BoxValueText { get => _boxValueText; set { _boxValueText = value; OnPropertyChanged(); } }

        private string _wordSpeak;
        public string WordSpeak { get => _wordSpeak; set { _wordSpeak = value; OnPropertyChanged(); } }

        private string _fileName;
        public string FileName { get => _fileName; set { _fileName = value; OnPropertyChanged(); } }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

        public bool IsRecording => _isRecording;

        private ObservableCollection<VoiceCommand> _userCommands = new ObservableCollection<VoiceCommand>();
        public ObservableCollection<VoiceCommand> UserCommands { get => _userCommands; set { _userCommands = value; OnPropertyChanged(); } }

        public ICommand StartCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand FileBrowseCommand { get; }
        public ICommand OpenTxtCommand { get; }

        private async Task SpeakAndWaitAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _isSpeaking = true;
            try
            {
                await _speech.SpeakAsync(text);
            }
            finally
            {
                _isSpeaking = false;
            }
        }

        private async void ExecuteStart()
        {
            if (!_isRecording)
            {
                // Если микрофон доступен – пытаемся записать
                if (_microphoneAvailable)
                {
                    try
                    {
                        _audioRecorder.StartRecording(_tempAudioFile);
                        _isRecording = true;
                        Status = "Запись...";
                        OnPropertyChanged(nameof(IsRecording));
                    }
                    catch (Exception ex)
                    {
                        _microphoneAvailable = false;
                        Status = $"Ошибка микрофона: {ex.Message}";
                        System.Windows.MessageBox.Show(
                            $"Не удалось начать запись: {ex.Message}\n" +
                            "Пожалуйста, введите текст вручную в поле и нажмите GO.",
                            "Ошибка микрофона",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(BoxValueText))
                    {
                        await ProcessManualInput(BoxValueText);
                    }
                    else
                    {
                        Status = "Введите текст вручную";
                    }
                }
            }
            else
            {
                await StopAndProcessAsync();
            }
        }

        private async Task StopAndProcessAsync()
        {
            IsBusy = true;
            try
            {
                _audioRecorder.StopRecording();
                _isRecording = false;
                OnPropertyChanged(nameof(IsRecording));

                string transcript;
                if (System.IO.File.Exists(_tempAudioFile) && new System.IO.FileInfo(_tempAudioFile).Length > 0)
                {
                    transcript = !string.IsNullOrWhiteSpace(BoxValueText)
                        ? BoxValueText
                        : await _whisper.TranscribeAsync(_tempAudioFile);
                }
                else
                {
                    transcript = BoxValueText;
                }

                if (string.IsNullOrWhiteSpace(transcript))
                {
                    Status = "Введите текст вручную.";
                    return;
                }

                Status = "Обработка команды...";
                await ProcessVoiceCommand(transcript);
            }
            catch (Exception ex)
            {
                Status = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // метод для обработки ручного ввода
        private async Task ProcessManualInput(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Status = "Введите текст.";
                return;
            }
            IsBusy = true;
            try
            {
                Status = "Обработка ручного ввода...";
                await ProcessVoiceCommand(text);
            }
            catch (Exception ex)
            {
                Status = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ProcessVoiceCommand(string text)
        {
            string low = text.ToLower().Replace(".", "");

            if (low.Contains("открой") || low.Contains("запусти") || low.Contains("стартуй"))
            {
                var match = _userCommands.FirstOrDefault(c => low.Contains(c.Phrase.ToLower()));
                if (match != null)
                {
                    try { _processLauncher.LaunchFile(match.Path); return; }
                    catch { await SpeakAndWaitAsync("Не удалось открыть программу"); return; }
                }
                await SpeakAndWaitAsync("Такого приложения нету");
                return;
            }

            if (low.Contains("найди") || low.Contains("поищи") || low.Contains("загугли"))
            {
                string query = low.Replace("найди", "").Replace("поищи", "").Replace("загугли", "")
                                  .Replace("в интернете", "").Replace("пожалуйста", "").Trim();
                if (!string.IsNullOrWhiteSpace(query))
                    _processLauncher.LaunchUrl($"https://www.google.com/search?q={Uri.EscapeDataString(query)}");
                return;
            }

            Status = "Отправка запроса в Ollama...";
            string response = await _ollama.GenerateResponseAsync(text);
            BoxOllamaText = response;
            Status = "Ответ получен";
            await SpeakAndWaitAsync(response);
        }

        private async void ExecuteSave()
        {
            string phrase = WordSpeak?.Trim();
            string path = FileName?.Trim();
            if (string.IsNullOrEmpty(phrase) || string.IsNullOrEmpty(path))
            {
                Status = "Введите фразу и выберите EXE файл.";
                return;
            }

            var existing = _userCommands.FirstOrDefault(c => string.Equals(c.Phrase, phrase, StringComparison.OrdinalIgnoreCase));
            if (existing != null) _userCommands.Remove(existing);

            _userCommands.Add(new VoiceCommand { Phrase = phrase, Path = path });
            await _commandsRepo.SaveAsync(_userCommands.ToList());

            WordSpeak = "";
            FileName = "";
            Status = "Команда сохранена";
        }

        private void ExecuteFileBrowse()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "EXE файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dialog.ShowDialog() == true)
                FileName = dialog.FileName;
        }

        private void ExecuteOpenTxt()
        {
            string filePath = "commands.json";
            if (System.IO.File.Exists(filePath))
                _processLauncher.LaunchFile(filePath);
            else
                Status = "Файл с командами ещё не создан.";
        }

        private async Task LoadCommandsAsync()
        {
            var list = await _commandsRepo.LoadAsync();
            _userCommands.Clear();
            foreach (var cmd in list) _userCommands.Add(cmd);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;
        public RelayCommand(Action execute, Func<bool> canExecute = null) { _execute = execute; _canExecute = canExecute; }
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
        public void Execute(object parameter) => _execute();
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}