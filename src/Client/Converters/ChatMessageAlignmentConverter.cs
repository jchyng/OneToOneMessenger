using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace OneToOneMessenger_Client.Converters;

public sealed class ChatMessageAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is string sender && sender == "철수"
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
