using AIDictationApp.Views;
using Microsoft.UI.Xaml;

namespace AIDictationApp
{
    public partial class App : Application
    {
        public static Window? MainAppWindow;

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainAppWindow = new MainWindow();
            MainAppWindow.Activate();
        }
    }
}