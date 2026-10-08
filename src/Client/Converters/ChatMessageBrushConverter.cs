using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace OneToOneMessenger_Client.Converters;

/// <summary>
/// IsMine=true  → 발신 버블 그러데이션 (하늘색→파랑)
/// IsMine=false → 수신 버블 흰색 SolidColorBrush
/// </summary>
public sealed class ChatMessageBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is true)
        {
            // 발신: 텔레그램 스타일 대각선 그러데이션
            return new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint   = new Windows.Foundation.Point(1, 1),
                GradientStops =
                [
                    new GradientStop { Color = ColorHelper.FromArgb(0xFF, 0x64, 0xB8, 0xFF), Offset = 0.0 },
                    new GradientStop { Color = ColorHelper.FromArgb(0xFF, 0x3D, 0x7E, 0xFF), Offset = 1.0 },
                ]
            };
        }

        // 수신: 흰색 카드
        return new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
