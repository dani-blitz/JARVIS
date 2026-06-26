using System.Windows;
using System.Windows.Input;

namespace JARVIS
{
    public partial class FloatingButtonWindow : Window
    {
        public FloatingButtonWindow()
        {
            InitializeComponent();
        }

        private void FloatingButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                this.DragMove();
        }
    }
}