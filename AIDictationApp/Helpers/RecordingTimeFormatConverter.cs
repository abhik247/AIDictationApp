using Microsoft.UI.Xaml.Data;
using System;

namespace AIDictationApp.Helpers
{
    public class RecordingTimeFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string time)
            {
                return $"({time})";
            }
            return "(00:00)";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
