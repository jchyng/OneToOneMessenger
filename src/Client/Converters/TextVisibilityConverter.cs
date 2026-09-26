using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace OneToOneMessenger_Client.Converters;

public sealed class TextVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string text && !string.IsNullOrWhiteSpace(text)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
