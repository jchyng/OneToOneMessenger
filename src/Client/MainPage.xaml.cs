using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OneToOneMessenger_Client.Services;
using Shared;
using Windows.System;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OneToOneMessenger_Client;

public sealed partial class MainPage : Page
{
    private const string UserName = "철수";
    private readonly ChatApiService _apiService = new();
    private readonly ChatHubService _hubService = new(UserName);
    private readonly ClientSettingsService _settingsService = new();
    private readonly NotificationService _notificationService = new();
    private readonly ObservableCollection<MessageDto> _messages = new();
    private readonly ObservableCollection<VaultFileDto> _vaultFiles = new();
    private readonly ObservableCollection<SearchResultDto> _searchResults = new();
    private readonly List<StorageFile> _failedFiles = new();
    private string? _vaultCategory;
    private string _vaultSort = "newest";

    public MainPage()
    {
        InitializeComponent();
        MessagesList.ItemsSource = _messages;
        VaultList.ItemsSource = _vaultFiles;
        SearchResultsList.ItemsSource = _searchResults;
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    private async void MainPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _hubService.MessageReceived += OnMessageReceived;
        _hubService.PeerPresenceChanged += OnPresenceChanged;
        _hubService.MessagesRead += OnMessagesRead;
        _hubService.ConnectionStateChanged += OnConnectionStateChanged;
        try
        {
            var messages = await _apiService.GetMessagesAsync();
            if (messages is not null)
            {
                foreach (var message in messages)
                {
                    _messages.Add(message);
                }
            }

            await LoadVaultAsync();
            await LoadVaultSummaryAsync();
            await _hubService.StartAsync();
            var unreadSequences = _messages
                .Where(message => message.Sender != UserName && !message.IsRead)
                .Select(message => message.Seq)
                .Where(seq => seq > 0)
                .ToArray();
            if (unreadSequences.Length > 0)
            {
                await _hubService.MarkReadAsync(unreadSequences);
            }
            UpdatePresence(true);
        }
        catch (Exception exception)
        {
            PresenceText.Text = $"연결 실패: {exception.Message}";
        }
    }

    private async void MainPage_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _hubService.MessageReceived -= OnMessageReceived;
        _hubService.PeerPresenceChanged -= OnPresenceChanged;
        _hubService.MessagesRead -= OnMessagesRead;
        _hubService.ConnectionStateChanged -= OnConnectionStateChanged;
        await _hubService.DisposeAsync();
    }

    private void OnMessageReceived(MessageDto message)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            _messages.Add(message);
            MessagesList.ScrollIntoView(message);
        });

        if (message.Sender != UserName)
        {
            if (_settingsService.NotificationsEnabled &&
                !_notificationService.TryShowMessage(message.Sender, message.Body, out var notificationError) &&
                notificationError is not null)
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                    PresenceText.Text = $"알림 실패: {notificationError}");
            }
            _ = MarkMessageReadAsync(message.Seq);
        }
    }

    private void OnPresenceChanged(bool online)
    {
        _ = DispatcherQueue.TryEnqueue(() => UpdatePresence(online));
    }

    private void OnConnectionStateChanged(string state)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            ConnectionBanner.Visibility = state == "connected"
                ? Microsoft.UI.Xaml.Visibility.Collapsed
                : Microsoft.UI.Xaml.Visibility.Visible;
            ConnectionBannerText.Text = state == "reconnecting"
                ? "연결 끊김 — 재연결 중…"
                : "연결 끊김";
            if (state == "connected")
            {
                PresenceText.Text = "연결됨";
            }
        });
    }

    private void UpdatePresence(bool online)
    {
        PresenceText.Text = online ? "온라인" : "오프라인";
        PresenceDot.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(
                255,
                online ? (byte)45 : (byte)160,
                online ? (byte)190 : (byte)170,
                online ? (byte)90 : (byte)180));
    }

    private void OnMessagesRead(string reader, long[] sequences, DateTimeOffset readAt)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var sequence in sequences)
            {
                var index = _messages
                    .Select((message, position) => (message, position))
                    .FirstOrDefault(item => item.message.Seq == sequence)
                    .position;
                if (index >= 0 && index < _messages.Count && _messages[index].Seq == sequence)
                {
                    _messages[index] = _messages[index] with { IsRead = true, ReadAt = readAt };
                }
            }
        });
    }

    private async Task MarkMessageReadAsync(long sequence)
    {
        if (sequence > 0)
        {
            await _hubService.MarkReadAsync(new[] { sequence });
        }
    }

    private async Task LoadVaultAsync(string? query = null)
    {
        var files = await _apiService.GetVaultAsync(
            category: _vaultCategory,
            query: query,
            sort: _vaultSort);
        _vaultFiles.Clear();
        if (files is not null)
        {
            foreach (var file in files)
            {
                _vaultFiles.Add(file);
            }
        }
    }

    private async void OpenVaultMessage_Click(
            object sender,
            Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is not Button { Tag: VaultFileDto file })
            {
                return;
            }

            var message = await FindMessageAsync(file.MessageSeq);
            if (message is null)
            {
                PresenceText.Text = "원본 메시지를 불러오지 못했습니다.";
                return;
            }

            MessagesList.ScrollIntoView(message);
            PresenceText.Text = $"원본 위치로 이동: {file.OriginalName}";
        }

    private async Task<MessageDto?> FindMessageAsync(long sequence)
        {
            var existing = _messages.FirstOrDefault(message => message.Seq == sequence);
            if (existing is not null)
            {
                return existing;
            }

            long? beforeSeq = _messages.Count == 0
                ? null
                : _messages.Min(message => message.Seq);
            for (var page = 0; page < 20; page++)
            {
                var messages = await _apiService.GetMessagesAsync(
                    limit: 100,
                    beforeSeq: beforeSeq);
                if (messages is null || messages.Count == 0)
                {
                    break;
                }

                foreach (var message in messages)
                {
                    if (_messages.All(existingMessage => existingMessage.Seq != message.Seq))
                    {
                        _messages.Add(message);
                    }
                }

                existing = _messages.FirstOrDefault(message => message.Seq == sequence);
                if (existing is not null)
                {
                    return existing;
                }

                var nextBeforeSeq = messages.Min(message => message.Seq);
                if (beforeSeq == nextBeforeSeq)
                {
                    break;
                }

                beforeSeq = nextBeforeSeq;
            }

            return null;
    }

    private async Task LoadVaultSummaryAsync()
        {
            var summary = await _apiService.GetVaultSummaryAsync();
            if (summary is not null)
            {
                VaultSummaryText.Text =
                    $"이미지 {summary.Images} · 영상 {summary.Videos} · 문서 {summary.Docs} · 기타 {summary.Etc}";
        }
    }

    private async void SendButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async void MessageInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        var body = MessageInput.Text.Trim();
        if (body.Length == 0)
        {
            return;
        }

        MessageInput.Text = string.Empty;
        await _hubService.SendMessageAsync(Guid.NewGuid(), body);
    }

    private async void SettingsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var notificationsCheckBox = new CheckBox
        {
            Content = "새 메시지 알림 사용",
            IsChecked = _settingsService.NotificationsEnabled
        };
        var downloadFolderTextBox = new TextBox
        {
            Header = "다운로드 폴더",
            Text = _settingsService.DownloadFolder,
            IsReadOnly = true
        };
        var chooseFolderButton = new Button { Content = "폴더 선택" };
        chooseFolderButton.Click += async (_, _) =>
        {
            if (App.CurrentWindow is null)
            {
                return;
            }

            var picker = new FolderPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow));
            picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                downloadFolderTextBox.Text = folder.Path;
            }
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(notificationsCheckBox);
        content.Children.Add(downloadFolderTextBox);
        content.Children.Add(chooseFolderButton);

        var dialog = new ContentDialog
        {
            Title = "설정",
            Content = content,
            PrimaryButtonText = "저장",
            CloseButtonText = "취소",
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _settingsService.NotificationsEnabled = notificationsCheckBox.IsChecked == true;
            _settingsService.DownloadFolder = downloadFolderTextBox.Text;
        }
    }

    private void SearchButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SearchOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        MessageSearchInput.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }

    private void CloseSearchButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SearchOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        MessageSearchInput.Text = string.Empty;
        _searchResults.Clear();
        SearchStatusText.Text = string.Empty;
    }

    private void SearchKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        MessageSearchInput.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        args.Handled = true;
    }

    private async void MessageSearchInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            CloseSearchButton_Click(sender, new Microsoft.UI.Xaml.RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        await SearchMessagesAsync();
    }

    private async Task SearchMessagesAsync()
    {
        var query = MessageSearchInput.Text.Trim();
        _searchResults.Clear();
        if (query.Length == 0)
        {
            SearchStatusText.Text = string.Empty;
            return;
        }

        try
        {
            var results = await _apiService.SearchMessagesAsync(query);
            if (results is not null)
            {
                foreach (var result in results)
                {
                    _searchResults.Add(result);
                }
            }

            SearchStatusText.Text = _searchResults.Count == 0
                ? "검색 결과가 없습니다."
                : $"{_searchResults.Count}건";
        }
        catch (Exception exception)
        {
            SearchStatusText.Text = $"검색 실패: {exception.Message}";
        }
    }

    private void SearchResultsList_ItemClick(
        object sender,
        ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SearchResultDto result)
        {
            return;
        }

        var existing = _messages.FirstOrDefault(message => message.Seq == result.Msg.Seq);
        if (existing is null)
        {
            var insertAt = -1;
            for (var index = 0; index < _messages.Count; index++)
            {
                if (_messages[index].Seq > result.Msg.Seq)
                {
                    insertAt = index;
                    break;
                }
            }

            if (insertAt < 0)
            {
                _messages.Add(result.Msg);
            }
            else
            {
                _messages.Insert(insertAt, result.Msg);
            }
            existing = result.Msg;
        }

        MessagesList.ScrollIntoView(existing);
        SearchOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private async void VaultSearchInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await LoadVaultAsync(VaultSearchInput.Text);
        }
    }

    private async void VaultCategory_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string category })
        {
            _vaultCategory = string.IsNullOrWhiteSpace(category) ? null : category;
            await LoadVaultAsync(VaultSearchInput.Text);
        }
    }

    private async void VaultSort_Changed(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (VaultSortComboBox.SelectedItem is ComboBoxItem { Tag: string sort })
        {
            _vaultSort = sort;
            await LoadVaultAsync(VaultSearchInput.Text);
        }
    }

    private async void ChooseFile_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (App.CurrentWindow is null)
        {
            return;
        }

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow));
        picker.FileTypeFilter.Add("*");
        var files = await picker.PickMultipleFilesAsync();
        foreach (var file in files)
        {
            await UploadFileAsync(file);
        }
    }

    private async Task UploadFileAsync(StorageFile file)
    {
        UploadProgress.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        UploadProgress.Value = 0;
        try
        {
            var progress = new Progress<double>(value => UploadProgress.Value = value * 100);
            var message = await _apiService.UploadFileAsync(file, UserName, progress);
            if (!_messages.Any(item => item.Id == message.Id))
            {
                _messages.Add(message);
                MessagesList.ScrollIntoView(message);
            }
            await LoadVaultAsync();
            _failedFiles.Remove(file);
            RetryButton.Visibility = _failedFiles.Count == 0
                ? Microsoft.UI.Xaml.Visibility.Collapsed
                : Microsoft.UI.Xaml.Visibility.Visible;
        }
        catch (Exception exception)
        {
            _failedFiles.Add(file);
            RetryButton.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            PresenceText.Text = $"파일 전송 실패: {exception.Message}";
        }
        finally
        {
            UploadProgress.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }

    private async void RetryButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var pending = _failedFiles.ToArray();
            foreach (var file in pending)
            {
                await UploadFileAsync(file);
            }
        }

    private void ChatArea_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }

    private async void ChatArea_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                return;
            }

            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items.OfType<StorageFile>())
            {
                await UploadFileAsync(item);
            }
        }

    private async void PreviewImage_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is not Image { Tag: Guid fileId } || App.CurrentWindow is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "이미지 미리보기",
                CloseButtonText = "닫기",
                XamlRoot = Content.XamlRoot,
                Content = new Image
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                        new Uri($"http://localhost:5000/api/files/{fileId}/download")),
                    MaxWidth = 900,
                    MaxHeight = 650,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                }
            };
            await dialog.ShowAsync();
    }

    private void PreviewImage_Failed(object sender, Microsoft.UI.Xaml.ExceptionRoutedEventArgs e)
    {
        if (sender is not Image image || image.Parent is not Grid previewContainer)
        {
            return;
        }

        image.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        if (previewContainer.Children.Count > 1 &&
            previewContainer.Children[1] is Microsoft.UI.Xaml.FrameworkElement placeholder)
        {
            placeholder.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
    }

    private async void DownloadFile_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FileDto file })
        {
            return;
        }

        var folderPath = _settingsService.DownloadFolder;
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        var safeName = Path.GetFileName(file.OriginalName);
        var destinationPath = Path.Combine(folderPath, safeName);
        if (File.Exists(destinationPath))
        {
            var name = Path.GetFileNameWithoutExtension(safeName);
            var extension = Path.GetExtension(safeName);
            destinationPath = Path.Combine(
                folderPath,
                $"{name}-{DateTime.Now:yyyyMMddHHmmss}{extension}");
        }

        var progress = new Progress<double>(value => UploadProgress.Value = value * 100);
        UploadProgress.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        try
        {
            await _apiService.DownloadFileAsync(file.Id, destinationPath, progress);
            PresenceText.Text = $"다운로드 완료: {file.OriginalName}";
            if (_settingsService.NotificationsEnabled &&
                !_notificationService.TryShow(
                    "다운로드 완료",
                    file.OriginalName,
                    out var notificationError) &&
                notificationError is not null)
            {
                PresenceText.Text = $"알림 실패: {notificationError}";
            }
        }
        catch (Exception exception)
        {
            PresenceText.Text = $"다운로드 실패: {exception.Message}";
        }
        finally
        {
            UploadProgress.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }

    private async void SaveAsFile_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FileDto file } || App.CurrentWindow is null)
        {
            return;
        }

        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow));
        picker.SuggestedFileName = Path.GetFileName(file.OriginalName);
        picker.FileTypeChoices.Add("모든 파일", new List<string> { "*" });
        var destination = await picker.PickSaveFileAsync();
        if (destination is null)
        {
            return;
        }

        var progress = new Progress<double>(value => UploadProgress.Value = value * 100);
        UploadProgress.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        try
        {
            await _apiService.DownloadFileAsync(file.Id, destination.Path, progress);
            PresenceText.Text = $"저장 완료: {file.OriginalName}";
            if (_settingsService.NotificationsEnabled &&
                !_notificationService.TryShow(
                    "다운로드 완료",
                    file.OriginalName,
                    out var notificationError) &&
                notificationError is not null)
            {
                PresenceText.Text = $"알림 실패: {notificationError}";
            }
        }
        catch (Exception exception)
        {
            PresenceText.Text = $"저장 실패: {exception.Message}";
        }
        finally
        {
            UploadProgress.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }
}
