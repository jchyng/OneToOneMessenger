using System.Text.Json;
using Windows.Storage;

namespace OneToOneMessenger_Client.Services;

public sealed class ClientSettingsService
{
    private const string NotificationsKey = "notificationsEnabled";
    private const string DownloadFolderKey = "downloadFolder";
    private const string AvatarPathKey = "avatarPath";
    private const string UserNameKey = "userName";
    private readonly ApplicationDataContainer? _settings;
    private readonly string? _settingsFilePath;
    private SettingsState _fileSettings = new();

    public ClientSettingsService()
    {
        try
        {
            _settings = ApplicationData.Current.LocalSettings;
        }
        catch (InvalidOperationException)
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OneToOneMessenger");
            Directory.CreateDirectory(directory);
            _settingsFilePath = Path.Combine(directory, "settings.json");
            LoadFileSettings();
        }
    }

    public bool NotificationsEnabled
    {
        get
        {
            if (_settings is not null)
            {
                return _settings.Values.TryGetValue(NotificationsKey, out var value) &&
                       value is bool enabled
                    ? enabled
                    : true;
            }

            return _fileSettings.NotificationsEnabled;
        }
        set
        {
            if (_settings is not null)
            {
                _settings.Values[NotificationsKey] = value;
                return;
            }

            _fileSettings.NotificationsEnabled = value;
            SaveFileSettings();
        }
    }

    public string? UserName
    {
        get
        {
            if (_settings is not null)
            {
                return _settings.Values.TryGetValue(UserNameKey, out var value) && value is string name
                    ? name
                    : null;
            }

            return _fileSettings.UserName;
        }
        set
        {
            if (_settings is not null)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _settings.Values.Remove(UserNameKey);
                }
                else
                {
                    _settings.Values[UserNameKey] = value;
                }
                return;
            }

            _fileSettings.UserName = value;
            SaveFileSettings();
        }
    }

    public string? AvatarPath
    {
        get
        {
            if (_settings is not null)
            {
                return _settings.Values.TryGetValue(AvatarPathKey, out var value) &&
                       value is string path &&
                       !string.IsNullOrWhiteSpace(path)
                    ? path
                    : null;
            }

            return _fileSettings.AvatarPath;
        }
        set
        {
            if (_settings is not null)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _settings.Values.Remove(AvatarPathKey);
                }
                else
                {
                    _settings.Values[AvatarPathKey] = value;
                }
                return;
            }

            _fileSettings.AvatarPath = value;
            SaveFileSettings();
        }
    }

    public string DownloadFolder
    {
        get
        {
            if (_settings is not null)
            {
                return _settings.Values.TryGetValue(DownloadFolderKey, out var value) &&
                       value is string folder &&
                       !string.IsNullOrWhiteSpace(folder)
                    ? folder
                    : GetDefaultDownloadFolder();
            }

            return string.IsNullOrWhiteSpace(_fileSettings.DownloadFolder)
                ? GetDefaultDownloadFolder()
                : _fileSettings.DownloadFolder;
        }
        set
        {
            if (_settings is not null)
            {
                _settings.Values[DownloadFolderKey] = value;
                return;
            }

            _fileSettings.DownloadFolder = value;
            SaveFileSettings();
        }
    }

    private static string GetDefaultDownloadFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

    private void LoadFileSettings()
    {
        if (_settingsFilePath is null || !File.Exists(_settingsFilePath))
        {
            return;
        }

        try
        {
            _fileSettings = JsonSerializer.Deserialize<SettingsState>(
                File.ReadAllText(_settingsFilePath)) ?? new SettingsState();
        }
        catch (JsonException)
        {
            _fileSettings = new SettingsState();
        }
    }

    private void SaveFileSettings()
    {
        if (_settingsFilePath is not null)
        {
            File.WriteAllText(
                _settingsFilePath,
                JsonSerializer.Serialize(_fileSettings, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private sealed class SettingsState
    {
        public bool NotificationsEnabled { get; set; } = true;
        public string? DownloadFolder { get; set; }
        public string? AvatarPath { get; set; }
        public string? UserName { get; set; }
    }
}
