using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Shared;

namespace OneToOneMessenger_Client.Converters;

public sealed class FileCategoryVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value switch
        {
            FileDto => Visibility.Visible,
            string category when !string.IsNullOrWhiteSpace(category) => Visibility.Visible,
            _ => Visibility.Collapsed
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
