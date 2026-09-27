using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Shared;

namespace OneToOneMessenger_Client.Converters;

public sealed class ImageCategoryVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var category = value switch
        {
            FileDto file => file.Category,
            string text => text,
            _ => null
        };

        return category?.Equals("image", StringComparison.OrdinalIgnoreCase) == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
