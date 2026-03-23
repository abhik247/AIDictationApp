using AIDictationApp.Helpers;
using AIDictationApp.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Drawing;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace AIDictationApp.Views
{
    public sealed partial class MainWindow : Window
    {
        private readonly MicaBackdrop _micaBackdrop;

        private delegate IntPtr WinProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private WinProc? newWndProc;
        private IntPtr oldWndProc = IntPtr.Zero;
        private bool _hotkeyRegistered;
        private WinForms.NotifyIcon? _trayIcon;
        private FloatingRecorderButtonForm? _floatingButton;
        private bool _isExitingFromTray;
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            this.InitializeComponent();

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

            _vm.PropertyChanged += Vm_PropertyChanged;

            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.DataContext = _vm;
            }

            _micaBackdrop = new MicaBackdrop();
            this.SystemBackdrop = _micaBackdrop;

            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(700, 800));
            this.AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico"));
            ApplyAlwaysOnTop(_vm.AlwaysOnTop);
            InitializeTrayIcon();
            InitializeFloatingButton();

            this.AppWindow.Closing += AppWindow_Closing;
            this.Closed += MainWindow_Closed;
            this.Activated += MainWindow_Activated;
        }

        private void InitializeFloatingButton()
        {
            _floatingButton = new FloatingRecorderButtonForm();
            _floatingButton.ToggleRequested += (_, _) => ExecuteRecordingToggle();
            _floatingButton.SetRecordingState(false);
            _floatingButton.Hide();
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.State))
            {
                _floatingButton?.SetRecordingState(_vm.State == RecordingState.Recording);
            }
        }

        private void ExecuteRecordingToggle()
        {
            if (_vm.State == RecordingState.Recording)
            {
                if (_vm.SendAndInsertCommand.CanExecute(null))
                    _vm.SendAndInsertCommand.Execute(null);
            }
            else
            {
                if (_vm.RecordCommand.CanExecute(null))
                    _vm.RecordCommand.Execute(null);
            }
        }

        private void ShowFloatingButton()
        {
            if (_floatingButton == null || _floatingButton.Visible)
                return;

            _floatingButton.Show();
        }

        private void HideFloatingButton()
        {
            if (_floatingButton == null || !_floatingButton.Visible)
                return;

            _floatingButton.Hide();
        }

        private void InitializeTrayIcon()
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
            var appIcon = System.IO.File.Exists(iconPath)
                ? new Icon(iconPath)
                : SystemIcons.Application;

            _trayIcon = new WinForms.NotifyIcon
            {
                Text = "AI Dictation",
                Icon = appIcon,
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
            HideFloatingButton();
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
            ShowFloatingButton();
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
            RestoreWindowProcedure();
            UnregisterHotkey();
            _vm.PropertyChanged -= Vm_PropertyChanged;
            DisposeFloatingButton();
            DisposeTrayIcon();
        }

        private void RestoreWindowProcedure()
        {
            if (oldWndProc == IntPtr.Zero)
                return;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetWindowLongPtr(hwnd, GWLP_WNDPROC, oldWndProc);
            oldWndProc = IntPtr.Zero;
            newWndProc = null;
        }

        private void UnregisterHotkey()
        {
            if (!_hotkeyRegistered)
                return;

            HotkeyManager.Unregister(this);
            _hotkeyRegistered = false;
        }

        private void DisposeTrayIcon()
        {
            if (_trayIcon == null)
                return;

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        private void DisposeFloatingButton()
        {
            if (_floatingButton == null)
                return;

            _floatingButton.Hide();
            _floatingButton.Dispose();
            _floatingButton = null;
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
            _hotkeyRegistered = true;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            newWndProc = new WinProc(WndProc);
            oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(newWndProc));
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            const int WM_HOTKEY = 0x0312;
            const int WM_SIZE = 0x0005;
            const int SIZE_MINIMIZED = 1;

            if (msg == WM_HOTKEY)
            {
                ExecuteRecordingToggle();
            }

            if (msg == WM_SIZE)
            {
                if (wParam.ToInt32() == SIZE_MINIMIZED)
                {
                    ShowFloatingButton();
                }
                else
                {
                    HideFloatingButton();
                }
            }

            if (oldWndProc != IntPtr.Zero)
            {
                return CallWindowProc(oldWndProc, hWnd, msg, wParam, lParam);
            }

            return IntPtr.Zero;
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