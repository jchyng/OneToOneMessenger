using System.Globalization;
using Microsoft.UI.Xaml.Data;

namespace OneToOneMessenger_Client.Converters;

public sealed class KoreanTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is DateTimeOffset timestamp
            ? timestamp.ToLocalTime().ToString("tt h:mm", CultureInfo.GetCultureInfo("ko-KR"))
            : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
