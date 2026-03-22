using AIDictationApp.Helpers;
using AIDictationApp.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Drawing;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace AIDictationApp.Views
{
    public sealed partial class MainWindow : Window
    {
        private MicaBackdrop micaBackdrop;

        private delegate IntPtr WinProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private WinProc? newWndProc;
        private IntPtr oldWndProc = IntPtr.Zero;
        private WinForms.NotifyIcon? _trayIcon;
        private bool _isExitingFromTray;

        // 🔥 NEW: ViewModel + Animation references
        private MainViewModel _vm;

        public MainWindow()
        {
            this.InitializeComponent();

            // 🔥 UPDATED: Store ViewModel reference
            _vm = new MainViewModel();

            _vm.OnTranscribedTextReceived = text =>
            {
                var tb = TranscriptTextBox;
                int start = tb.SelectionStart;
                string currentText = tb.Text ?? "";

                // Add a space if we are inserting after existing text that doesn't end with whitespace
                if (start > 0 && currentText.Length >= start && !char.IsWhiteSpace(currentText[start - 1]))
                {
                    text = " " + text;
                }

                string newText = currentText.Insert(start, text);
                tb.Text = newText;

                // Move cursor to end of inserted text
                tb.SelectionStart = start + text.Length;
            };

            _vm.GetSelectedTextAction = () =>
            {
                return TranscriptTextBox.SelectedText;
            };

            _vm.ReplaceSelectedTextAction = (newText) =>
            {
                var tb = TranscriptTextBox;
                int start = tb.SelectionStart;
                int length = tb.SelectionLength;
                string currentText = tb.Text ?? "";

                tb.Text = currentText.Remove(start, length).Insert(start, newText);
                tb.SelectionStart = start;
                tb.SelectionLength = newText.Length;
            };

            _vm.SelectAllAction = () =>
            {
                TranscriptTextBox.Focus(FocusState.Programmatic);
                TranscriptTextBox.SelectAll();
            };

            _vm.HideWindowAction = () =>
            {
                if (this.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.Minimize();
                }
            };

            _vm.SetAlwaysOnTopAction = isAlwaysOnTop =>
            {
                ApplyAlwaysOnTop(isAlwaysOnTop);
            };

            _vm.ShowMessageAction = async (title, message) =>
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await dialog.ShowAsync();
            };

            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.DataContext = _vm;
            }

            micaBackdrop = new MicaBackdrop();
            this.SystemBackdrop = micaBackdrop;

            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(700, 800));
            ApplyAlwaysOnTop(_vm.AlwaysOnTop);
            InitializeTrayIcon();

            this.AppWindow.Closing += AppWindow_Closing;
            this.Closed += MainWindow_Closed;
            this.Activated += MainWindow_Activated;
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new WinForms.NotifyIcon
            {
                Text = "AI Dictation",
                Icon = SystemIcons.Application,
                Visible = true
            };

            var contextMenu = new WinForms.ContextMenuStrip();
            contextMenu.Items.Add("Open", null, (_, _) => ShowFromTray());
            contextMenu.Items.Add("Exit", null, (_, _) => ExitFromTray());

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (_, _) => ShowFromTray();
        }

        private void ShowFromTray()
        {
            this.AppWindow.Show();
            if (this.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Restore();
            }

            this.Activate();
        }

        private void HideToTray()
        {
            this.AppWindow.Hide();
            _trayIcon?.ShowBalloonTip(1500, "AI Dictation", "Still running in the background. Use the tray icon to reopen.", WinForms.ToolTipIcon.Info);
        }

        private void ExitFromTray()
        {
            _isExitingFromTray = true;
            this.Close();
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (!_isExitingFromTray && _vm.KeepRunningInBackground)
            {
                args.Cancel = true;
                HideToTray();
                return;
            }

            DisposeTrayIcon();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            DisposeTrayIcon();
        }

        private void DisposeTrayIcon()
        {
            if (_trayIcon == null)
                return;

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        private void ApplyAlwaysOnTop(bool isAlwaysOnTop)
        {
            if (this.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = isAlwaysOnTop;
            }
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (oldWndProc != IntPtr.Zero)
                return;

            HotkeyManager.Register(this);
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            newWndProc = new WinProc(WndProc);
            oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(newWndProc));
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY)
            {
                if (_vm != null)
                {
                    if (_vm.RecordCommand.CanExecute(null))
                        _vm.RecordCommand.Execute(null);
                }
            }

            return CallWindowProc(oldWndProc, hWnd, msg, wParam, lParam);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Visible;
            ApiSettingsPanel.Visibility = Visibility.Collapsed;
            CustomizePromptsPanel.Visibility = Visibility.Collapsed;
        }

        private void CloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            ApiSettingsPanel.Visibility = Visibility.Collapsed;
            CustomizePromptsPanel.Visibility = Visibility.Collapsed;
        }

        private void OpenApiSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            ApiSettingsPanel.Visibility = Visibility.Visible;
            CustomizePromptsPanel.Visibility = Visibility.Collapsed;
        }

        private void OpenCustomizePrompts_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            ApiSettingsPanel.Visibility = Visibility.Collapsed;
            CustomizePromptsPanel.Visibility = Visibility.Visible;
        }

        private void BackToSettings_Click(object sender, RoutedEventArgs e)
        {
            ApiSettingsPanel.Visibility = Visibility.Collapsed;
            CustomizePromptsPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Visible;
        }

        private const int GWLP_WNDPROC = -4;

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            else
                return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    }
}