using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace OneToOneMessenger_Client.Converters;

public sealed class ImageCategoryVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is string category && category.Equals("image", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
