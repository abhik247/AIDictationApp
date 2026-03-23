using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace AIDictationApp.Helpers
{
    public class HotkeyManager
    {
        private const int HotkeyId = 1;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(
            IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        public static void Register(Window window)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

            RegisterHotKey(hwnd, HotkeyId, MOD_CONTROL | MOD_SHIFT, (uint)'D');
        }

        public static void Unregister(Window window)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            UnregisterHotKey(hwnd, HotkeyId);
        }
    }
}