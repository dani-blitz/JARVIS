using System;
using System.Windows;

namespace JARVIS
{
    public partial class MainWindow : Window
    {
        private FloatingButtonWindow _floatingWindow;

        public MainWindow()
        {
            InitializeComponent();

            var audioRecorder = new AudioRecorder();
            var whisper = new WhisperTranscriber();
            var ollama = new OllamaService();
            var speech = new SpeechSynthesizerService();
            var processLauncher = new ProcessLauncher();
            var commandsRepo = new CommandsRepository();

            DataContext = new MainViewModel(
                audioRecorder,
                whisper,
                ollama,
                speech,
                processLauncher,
                commandsRepo);

            _floatingWindow = new FloatingButtonWindow();
            _floatingWindow.DataContext = this.DataContext;

            this.StateChanged += MainWindow_StateChanged;
            this.Closing += (s, e) => _floatingWindow?.Close();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                if (_floatingWindow != null && !_floatingWindow.IsVisible)
                {
                    var workArea = SystemParameters.WorkArea;
                    _floatingWindow.Left = workArea.Right - _floatingWindow.Width - 20;
                    _floatingWindow.Top = workArea.Bottom - _floatingWindow.Height - 20;
                    _floatingWindow.Show();
                }
            }
            else
            {
                if (_floatingWindow != null && _floatingWindow.IsVisible)
                    _floatingWindow.Hide();
            }
        }
    }
}