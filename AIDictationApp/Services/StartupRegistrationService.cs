using System;
using Microsoft.Win32;

namespace AIDictationApp.Services
{
    public class StartupRegistrationService
    {
        private const string StartupKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "AIDictationApp";

        public bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, false);
                var value = key?.GetValue(StartupValueName) as string;
                return !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }

        public bool SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(StartupKeyPath, true);
                if (key == null)
                    return false;

                if (enabled)
                {
                    var executablePath = Environment.ProcessPath;
                    if (string.IsNullOrWhiteSpace(executablePath))
                        return false;

                    key.SetValue(StartupValueName, $"\"{executablePath}\"");
                }
                else
                {
                    key.DeleteValue(StartupValueName, false);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
