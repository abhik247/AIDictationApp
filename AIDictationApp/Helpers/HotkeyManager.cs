using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace AIDictationApp.Helpers
{
    public class HotkeyManager
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(
            IntPtr hWnd, int id, uint fsModifiers, uint vk);

        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        public static void Register(Window window)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

            RegisterHotKey(hwnd, 1, MOD_CONTROL | MOD_SHIFT, (uint)'D');
        }
    }
}