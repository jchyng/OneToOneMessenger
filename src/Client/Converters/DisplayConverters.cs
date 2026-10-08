using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace OneToOneMessenger_Client.Converters;

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is true;
        if (parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class StringNullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasValue = value is string s && !string.IsNullOrWhiteSpace(s);
        if (parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class ChatBubbleCornerRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true
            // 발신: 우측 하단은 꼬리 Path가 채우므로 작은 반경
            ? new CornerRadius(18, 18, 4, 18)
            // 수신: 좌측 하단은 꼬리 Path가 채우므로 작은 반경
            : new CornerRadius(18, 18, 18, 4);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>발신(true) → White / 수신(false) → #2C3E55</summary>
public sealed class BubbleTextForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true
            ? new SolidColorBrush(Microsoft.UI.Colors.White)
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x2C, 0x3E, 0x55));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>발신 꼬리 색 브러시 반환 (발신=그러데이션 끝색, 수신=흰색)</summary>
public sealed class BubbleTailBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true
            ? (object)new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x3D, 0x7E, 0xFF))
            : new SolidColorBrush(Microsoft.UI.Colors.White);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>수신 버블에만 1px 테두리 적용 (발신=0)</summary>
public sealed class BubbleBorderThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? new Thickness(0) : new Thickness(1);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not long bytes || bytes < 0)
        {
            return string.Empty;
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        var format = unit == 0 || size >= 100 ? "0" : size >= 10 ? "0.0" : "0.##";
        return $"{size.ToString(format, CultureInfo.CurrentCulture)} {units[unit]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class FileCategoryLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string category
            ? category.ToLowerInvariant() switch
            {
                "image" => "이미지",
                "video" => "영상",
                "doc" => "문서",
                _ => "기타"
            }
            : "파일";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class FileCategoryGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string category
            ? category.ToLowerInvariant() switch
            {
                "image" => "\uEB9F",
                "video" => "\uE714",
                "doc" => "\uE8A5",
                _ => "\uE7C3"
            }
            : "\uE7C3";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// 파일 유형별 아이콘 배경 브러시 반환.
/// parameter="fg" 로 호출하면 전경(아이콘) 색 반환.
/// </summary>
public sealed class FileCategoryIconBrushConverter : IValueConverter
{
    // (배경색, 전경색) 쌍
    private static readonly Dictionary<string, (string Bg, string Fg)> Palette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image"] = ("#E3F1FF", "#288AD6"),
        ["video"] = ("#FCEEF4", "#C94D7C"),
        ["doc"]   = ("#EEEEFF", "#5B56D8"),
        ["etc"]   = ("#F0F2F5", "#697386"),
    };

    private static SolidColorBrush FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        var a = hex.Length == 8 ? System.Convert.ToByte(hex[..2], 16) : (byte)0xFF;
        var offset = hex.Length == 8 ? 2 : 0;
        var r = System.Convert.ToByte(hex.Substring(offset, 2), 16);
        var g = System.Convert.ToByte(hex.Substring(offset + 2, 2), 16);
        var b = System.Convert.ToByte(hex.Substring(offset + 4, 2), 16);
        return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
    }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = (value as string ?? "etc").ToLowerInvariant();
        if (!Palette.TryGetValue(key, out var pair))
            pair = Palette["etc"];

        var isFg = parameter is string p && p.Equals("fg", StringComparison.OrdinalIgnoreCase);
        return FromHex(isFg ? pair.Fg : pair.Bg);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class FriendlyDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not DateTimeOffset timestamp)
        {
            return string.Empty;
        }

        var date = timestamp.ToLocalTime().Date;
        var today = DateTime.Today;
        if (date == today)
        {
            return "오늘";
        }

        if (date == today.AddDays(-1))
        {
            return "어제";
        }

        return date.Year == today.Year
            ? date.ToString("M월 d일", CultureInfo.GetCultureInfo("ko-KR"))
            : date.ToString("yyyy.MM.dd", CultureInfo.GetCultureInfo("ko-KR"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed partial class SearchPreviewConverter : IValueConverter
{
    [GeneratedRegex("</?mark>", RegexOptions.IgnoreCase)]
    private static partial Regex MarkTagRegex();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string preview)
        {
            return string.Empty;
        }

        return WebUtility.HtmlDecode(MarkTagRegex().Replace(preview, string.Empty));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class AvatarImageSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return DependencyProperty.UnsetValue;
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            return new BitmapImage(uri);
        }

        return DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
