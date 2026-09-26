using Windows.Storage;

namespace OneToOneMessenger_Client.Services;

public sealed class ClientSettingsService
{
    private const string NotificationsKey = "notificationsEnabled";
    private const string DownloadFolderKey = "downloadFolder";
    private readonly ApplicationDataContainer _settings = ApplicationData.Current.LocalSettings;

    public bool NotificationsEnabled
    {
        get => _settings.Values.TryGetValue(NotificationsKey, out var value) &&
               value is bool enabled
            ? enabled
            : true;
        set => _settings.Values[NotificationsKey] = value;
    }

    public string DownloadFolder
    {
        get => _settings.Values.TryGetValue(DownloadFolderKey, out var value) &&
               value is string folder &&
               !string.IsNullOrWhiteSpace(folder)
            ? folder
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        set => _settings.Values[DownloadFolderKey] = value;
    }
}
