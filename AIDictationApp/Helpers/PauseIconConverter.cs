using Microsoft.UI.Xaml.Data;
using System;

namespace AIDictationApp.Helpers
{
    public class PauseIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isPaused)
            {
                return isPaused ? "▶" : "⏸";
            }
            return "⏸";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
