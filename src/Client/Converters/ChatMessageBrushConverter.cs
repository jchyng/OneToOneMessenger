using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace OneToOneMessenger_Client.Converters;

public sealed class ChatMessageBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var color = value is true
            ? ColorHelper.FromArgb(0xFF, 0xE8, 0xF1, 0xFE)
            : ColorHelper.FromArgb(0xFF, 0xF1, 0xF3, 0xF7);
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
