using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace AIDictationApp.Helpers
{
    public class RecordedStateToVisibilityConverter : IValueConverter
    {
        // Show controls when state is Recording or Recorded
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is AIDictationApp.ViewModels.RecordingState state &&
                state != AIDictationApp.ViewModels.RecordingState.Idle)
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}