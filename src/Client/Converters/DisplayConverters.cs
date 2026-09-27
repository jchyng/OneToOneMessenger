using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

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

public sealed class ChatBubbleCornerRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string sender && sender == "철수"
            ? new CornerRadius(16, 16, 6, 16)
            : new CornerRadius(16, 16, 16, 6);

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
