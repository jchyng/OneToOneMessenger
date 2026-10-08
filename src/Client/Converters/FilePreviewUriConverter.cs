using Microsoft.UI.Xaml.Data;
using OneToOneMessenger_Client.Services;

namespace OneToOneMessenger_Client.Converters;

public sealed class FilePreviewUriConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is Guid id
            ? new Uri(new Uri(ServerEndpoint.Url), $"api/files/{id}/download")
            : null!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
