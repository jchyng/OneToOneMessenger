using Microsoft.UI.Xaml.Data;

namespace OneToOneMessenger_Client.Converters;

public sealed class ReadStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? "✓✓" : "✓";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
