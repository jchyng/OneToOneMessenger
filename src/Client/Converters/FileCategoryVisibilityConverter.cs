using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace OneToOneMessenger_Client.Converters;

public sealed class FileCategoryVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is string category && !string.IsNullOrWhiteSpace(category)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
