using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OneToOneMessenger_Client.Services;
using Shared;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace OneToOneMessenger_Client;

public sealed partial class MainPage : Page
{
    private const double CompactLayoutThreshold = 980;

    private readonly ChatApiService _apiService = new();
    private readonly ClientSettingsService _settingsService = new();
    private ChatHubService _hubService = null!;
    private string UserName => _settingsService.UserName ?? "철수";
    private string PartnerName => UserName == "철수" ? "짱구" : "철수";
    private readonly NotificationService _notificationService = new();
    private readonly ObservableCollection<MessageDto> _messages = new();
    private readonly ObservableCollection<ChatMessageItem> _timeline = new();
    private readonly ObservableCollection<VaultFileDto> _vaultFiles = new();
    private readonly ObservableCollection<SearchResultDto> _searchResults = new();
    private readonly List<StorageFile> _failedFiles = new();
    private readonly Dictionary<string, Guid> _uploadMessageIds = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _activeUploadCancellation;

    private CancellationTokenSource? _vaultSearchDebounce;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _statusTimer;
    private string? _vaultCategory;
    private string _vaultSort = "newest";
    private bool _pageLoaded;
    private bool _hubShutdown;
    private bool _isCompactLayout;
    private bool _wideVaultPreference = true;
    private bool _isImeComposing;

    public MainPage()
    {
        InitializeComponent();
        MessagesList.ItemsSource = _timeline;
        VaultList.ItemsSource = _vaultFiles;
        SearchResultsList.ItemsSource = _searchResults;
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    private async Task<bool> EnsureUserSelectedAsync()
    {
        if (_settingsService.UserName is "철수" or "짱구")
        {
            return true;
        }

        var selection = new ComboBox
        {
            PlaceholderText = "이름 선택",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items = { "철수", "짱구" }
        };
        var content = new StackPanel { Spacing = 12, MinWidth = 320 };
        content.Children.Add(new TextBlock { Text = "이 기기에서 사용할 이름을 선택하세요." });
        content.Children.Add(selection);

        var dialog = new ContentDialog
        {
            Title = "처음 설정",
            Content = content,
            PrimaryButtonText = "시작",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };

        while (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (selection.SelectedItem is string name)
            {
                _settingsService.UserName = name;
                return true;
            }

            ShowStatus("사용할 이름을 선택해 주세요.", InfoBarSeverity.Warning);
        }

        return false;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_pageLoaded)
        {
            return;
        }

        _pageLoaded = true;
        if (!await EnsureUserSelectedAsync())
        {
            _pageLoaded = false;
            return;
        }

        _hubService = new ChatHubService(UserName);
        _hubService.MessageReceived += OnMessageReceived;
        _hubService.PeerPresenceChanged += OnPresenceChanged;
        _hubService.MessagesRead += OnMessagesRead;
        _hubService.ConnectionStateChanged += OnConnectionStateChanged;

        LoadAvatars();
        UpdateIdentityLabels();

        MessagesLoadingState.Visibility = Visibility.Visible;
        try
        {
            var messages = await _apiService.GetMessagesAsync();
            if (messages is not null)
            {
                foreach (var message in messages)
                {
                    AddOrReplaceMessage(message, rebuild: false);
                }
            }

            RebuildTimeline();
            UpdateMessageStates();
            ScrollToLatestMessage();
        }
        catch (Exception exception)
        {
            ShowStatus($"대화 내용을 불러오지 못했습니다: {exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            MessagesLoadingState.Visibility = Visibility.Collapsed;
            UpdateMessageStates();
        }

        try
        {
            await RefreshVaultAsync();
        }
        catch (Exception exception)
        {
            ShowStatus($"보관함을 불러오지 못했습니다: {exception.Message}", InfoBarSeverity.Error);
        }

        try
        {
            await _hubService.StartAsync();
            var unreadSequences = _messages
                .Where(message => message.Sender != UserName && !message.IsRead)
                .Select(message => message.Seq)
                .Where(sequence => sequence > 0)
                .ToArray();
            if (unreadSequences.Length > 0)
            {
                await _hubService.MarkReadAsync(unreadSequences);
            }

            PresenceText.Text = "서버 연결됨";
            PresenceDot.Fill = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(255, 32, 185, 104));
        }
        catch (Exception exception)
        {
            PresenceText.Text = "연결 안 됨";
            ConnectionBanner.Visibility = Visibility.Visible;
            ConnectionBannerText.Text = "서버에 연결할 수 없습니다. 잠시 후 다시 시도합니다.";
            ShowStatus($"서버 연결에 실패했습니다: {exception.Message}", InfoBarSeverity.Error);
        }

        MessageInput.Focus(FocusState.Programmatic);
    }

    private void LoadAvatars()
    {
        var partnerAvatarPath = _settingsService.AvatarPath;
        ApplyPartnerAvatar(partnerAvatarPath);
    }

    private void UpdateIdentityLabels()
    {
        PartnerNameText.Text = PartnerName;
        PartnerAvatarInitial.Text = PartnerName[..1];
        EmptyStatePartnerText.Text = $"{PartnerName}에게 첫 메시지나 파일을 보내세요";
    }

    private async Task SwitchIdentityAsync()
    {
        _hubService.MessageReceived -= OnMessageReceived;
        _hubService.PeerPresenceChanged -= OnPresenceChanged;
        _hubService.MessagesRead -= OnMessagesRead;
        _hubService.ConnectionStateChanged -= OnConnectionStateChanged;
        await _hubService.DisposeAsync();

        UpdateIdentityLabels();
        _messages.Clear();
        _timeline.Clear();

        _hubService = new ChatHubService(UserName);
        _hubService.MessageReceived += OnMessageReceived;
        _hubService.PeerPresenceChanged += OnPresenceChanged;
        _hubService.MessagesRead += OnMessagesRead;
        _hubService.ConnectionStateChanged += OnConnectionStateChanged;
        await _hubService.StartAsync();

        var messages = await _apiService.GetMessagesAsync();
        if (messages is not null)
        {
            foreach (var message in messages)
            {
                AddOrReplaceMessage(message, rebuild: false);
            }
        }

        RebuildTimeline();
        UpdateMessageStates();
        ScrollToLatestMessage();

        var unreadSequences = _messages
            .Where(message => message.Sender != UserName && !message.IsRead)
            .Select(message => message.Seq)
            .Where(sequence => sequence > 0)
            .ToArray();
        if (unreadSequences.Length > 0)
        {
            await _hubService.MarkReadAsync(unreadSequences);
        }
    }

    private void ApplyPartnerAvatar(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                var uri = new Uri(path, UriKind.Absolute);
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
                var brush = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
                PartnerAvatarImage.Fill = brush;
                PartnerAvatarImage.Visibility = Visibility.Visible;
                PartnerAvatarInitial.Visibility = Visibility.Collapsed;
                PartnerAvatarEllipse.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            catch
            {
            }
        }
        else
        {
            PartnerAvatarImage.Visibility = Visibility.Collapsed;
            PartnerAvatarInitial.Visibility = Visibility.Visible;
            PartnerAvatarEllipse.Fill = (Brush)Application.Current.Resources["BrushAccentSoft"];
        }
    }

    private async void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _pageLoaded = false;
        _vaultSearchDebounce?.Cancel();
        _statusTimer?.Stop();
        await ShutdownAsync();
    }

    public async Task ShutdownAsync()
    {
        if (_hubShutdown)
        {
            return;
        }

        _hubShutdown = true;
        if (_hubService is null)
        {
            return;
        }

        _hubService.MessageReceived -= OnMessageReceived;
        _hubService.PeerPresenceChanged -= OnPresenceChanged;
        _hubService.MessagesRead -= OnMessagesRead;
        _hubService.ConnectionStateChanged -= OnConnectionStateChanged;
        await _hubService.DisposeAsync();
    }

    private void AddOrReplaceMessage(MessageDto message, bool rebuild = true)
    {
        var existingIndex = -1;
        for (var index = 0; index < _messages.Count; index++)
        {
            if (_messages[index].Id == message.Id ||
                (message.Seq > 0 && _messages[index].Seq == message.Seq))
            {
                existingIndex = index;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            _messages[existingIndex] = message;
        }
        else
        {
            _messages.Add(message);
        }

        if (rebuild)
        {
            RebuildTimeline();
            UpdateMessageStates();
        }
    }

    private void RebuildTimeline()
    {
        _timeline.Clear();
        MessageDto? previous = null;
        var partnerAvatarPath = _settingsService.AvatarPath;

        foreach (var message in _messages.OrderBy(item => item.SentAt).ThenBy(item => item.Seq))
        {
            // Ignore malformed historical rows that contain neither text nor a file.
            if (string.IsNullOrWhiteSpace(message.Body) && message.File is null)
            {
                continue;
            }

            var localDate = message.SentAt.ToLocalTime().Date;
            var previousLocalDate = previous?.SentAt.ToLocalTime().Date;
            var showDate = previous is null || localDate != previousLocalDate;
            var isMine = message.Sender == UserName;
            var isGrouped = previous is not null &&
                            !showDate &&
                            previous.Sender == message.Sender &&
                            message.SentAt - previous.SentAt <= TimeSpan.FromMinutes(3);

            _timeline.Add(new ChatMessageItem(
                message,
                isMine,
                showDate,
                FormatDateHeader(localDate),
                showAvatar: !isMine && !isGrouped,
                showSender: !isMine && !isGrouped,
                groupMargin: new Thickness(0, isGrouped ? 2 : 10, 0, 0),
                avatarPath: isMine ? null : partnerAvatarPath,
                partnerInitial: PartnerName[..1]));

            previous = message;
        }
    }

    private static string FormatDateHeader(DateTime date)
    {
        var culture = CultureInfo.GetCultureInfo("ko-KR");
        var calendarDate = date.Year == DateTime.Today.Year
            ? date.ToString("M월 d일", culture)
            : date.ToString("yyyy년 M월 d일", culture);
        var prefix = date == DateTime.Today
            ? $"오늘, {calendarDate}"
            : date == DateTime.Today.AddDays(-1)
                ? $"어제, {calendarDate}"
                : calendarDate;
        return $"{prefix} {date.ToString("dddd", culture)}";
    }

    private void UpdateMessageStates()
    {
        MessagesEmptyState.Visibility =
            _timeline.Count == 0 && MessagesLoadingState.Visibility != Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ScrollToLatestMessage()
    {
        if (_timeline.Count == 0)
        {
            return;
        }

        var last = _timeline[^1];
        _ = DispatcherQueue.TryEnqueue(() => MessagesList.ScrollIntoView(last));
    }

    private void ScrollToMessage(long sequence, bool highlight = true)
    {
        var item = _timeline.FirstOrDefault(entry => entry.Message.Seq == sequence);
        if (item is null)
        {
            return;
        }

        MessagesList.ScrollIntoView(item);
        if (!highlight)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            if (MessagesList.ContainerFromItem(item) is not ListViewItem container)
            {
                return;
            }

            container.Background = (Brush)Application.Current.Resources["BrushAccentSoft"];
            await Task.Delay(1600);
            container.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        });
    }

    private void OnMessageReceived(MessageDto message)
    {
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            AddOrReplaceMessage(message);
            ScrollToMessage(message.Seq, highlight: false);

            if (message.File is not null)
            {
                await RefreshVaultAsync();
            }
        });

        if (message.Sender != UserName)
        {
            if (_settingsService.NotificationsEnabled &&
                !_notificationService.TryShowMessage(message.Sender, message.Body, out var notificationError) &&
                notificationError is not null)
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                    ShowStatus($"알림을 표시하지 못했습니다: {notificationError}", InfoBarSeverity.Warning));
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
                ? Visibility.Collapsed
                : Visibility.Visible;
            ConnectionBannerText.Text = state == "reconnecting"
                ? "연결이 끊겼습니다. 다시 연결하는 중…"
                : "서버와 연결이 끊겼습니다.";

            if (state == "connected")
            {
                PresenceText.Text = "서버 연결됨";
                PresenceDot.Fill = new SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 32, 185, 104));
            }
            else
            {
                PresenceText.Text = "연결 끊김";
                PresenceDot.Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 160, 170, 180));
            }
        });
    }

    private void UpdatePresence(bool online)
    {
        PresenceText.Text = online ? "온라인" : "오프라인";
        PresenceDot.Fill = new SolidColorBrush(
            online
                ? Microsoft.UI.ColorHelper.FromArgb(255, 32, 185, 104)
                : Microsoft.UI.ColorHelper.FromArgb(255, 160, 170, 180));
    }

    private void OnMessagesRead(string reader, long[] sequences, DateTimeOffset readAt)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            var changed = false;
            foreach (var sequence in sequences)
            {
                for (var index = 0; index < _messages.Count; index++)
                {
                    var message = _messages[index];
                    if (message.Seq != sequence ||
                        message.Sender != UserName ||
                        string.Equals(reader, UserName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    _messages[index] = message with { IsRead = true, ReadAt = readAt };
                    changed = true;
                    break;
                }
            }

            if (changed)
            {
                RebuildTimeline();
            }
        });
    }

    private async Task MarkMessageReadAsync(long sequence)
    {
        if (sequence > 0)
        {
            await _hubService.MarkReadAsync([sequence]);
        }
    }

    private async Task LoadVaultAsync(string? query = null)
    {
        var files = await _apiService.GetVaultAsync(
            category: _vaultCategory,
            query: string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
            sort: _vaultSort);

        _vaultFiles.Clear();
        if (files is not null)
        {
            foreach (var file in files)
            {
                _vaultFiles.Add(file);
            }
        }

        VaultEmptyState.Visibility = _vaultFiles.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecentFilesTitle.Text = _vaultFiles.Count == 0
            ? "최근 파일"
            : $"최근 파일 · {_vaultFiles.Count}";
    }

    private async Task LoadVaultSummaryAsync()
    {
        var summary = await _apiService.GetVaultSummaryAsync();
        if (summary is null)
        {
            return;
        }

        var total = summary.Images + summary.Videos + summary.Docs + summary.Etc;
        VaultSummaryText.Text = $"총 {total}개 파일";
        ImageSummaryCountText.Text = $"{summary.Images}개";
        VideoSummaryCountText.Text = $"{summary.Videos}개";
        DocumentSummaryCountText.Text = $"{summary.Docs}개";
        OtherSummaryCountText.Text = $"{summary.Etc}개";
    }

    private async Task RefreshVaultAsync(bool showFeedback = false)
    {
        VaultLoadingRing.IsActive = true;
        VaultLoadingRing.Visibility = Visibility.Visible;
        try
        {
            await LoadVaultAsync(VaultSearchInput.Text);
            await LoadVaultSummaryAsync();
            if (showFeedback)
            {
                ShowStatus("보관함을 새로 고쳤습니다.", InfoBarSeverity.Success);
            }
        }
        finally
        {
            VaultLoadingRing.IsActive = false;
            VaultLoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private async void OpenVaultMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: VaultFileDto file })
        {
            return;
        }

        try
        {
            var message = await FindMessageAsync(file.MessageSeq);
            if (message is null)
            {
                ShowStatus("원본 메시지를 불러오지 못했습니다.", InfoBarSeverity.Warning);
                return;
            }

            if (_isCompactLayout)
            {
                VaultSplitView.IsPaneOpen = false;
            }

            ScrollToMessage(message.Seq);
        }
        catch (Exception exception)
        {
            ShowStatus($"원본 메시지를 불러오지 못했습니다: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task<MessageDto?> FindMessageAsync(long sequence)
    {
        var existing = _messages.FirstOrDefault(message => message.Seq == sequence);
        if (existing is not null)
        {
            return existing;
        }

        long? beforeSequence = _messages.Count == 0
            ? null
            : _messages.Min(message => message.Seq);

        for (var page = 0; page < 20; page++)
        {
            var messages = await _apiService.GetMessagesAsync(limit: 100, beforeSeq: beforeSequence);
            if (messages is null || messages.Count == 0)
            {
                break;
            }

            foreach (var message in messages)
            {
                AddOrReplaceMessage(message, rebuild: false);
            }

            RebuildTimeline();
            UpdateMessageStates();
            existing = _messages.FirstOrDefault(message => message.Seq == sequence);
            if (existing is not null)
            {
                return existing;
            }

            var nextBeforeSequence = messages.Min(message => message.Seq);
            if (beforeSequence == nextBeforeSequence)
            {
                break;
            }

            beforeSequence = nextBeforeSequence;
        }

        return null;
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendMessageAsync();
    }

    private async void MessageInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || _isImeComposing)
        {
            return;
        }

        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if ((shiftState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
        {
            return;
        }

        e.Handled = true;
        await SendMessageAsync();
    }

    private void MessageInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        SendButton.IsEnabled = !string.IsNullOrWhiteSpace(MessageInput.Text);
    }

    private void MessageInput_TextCompositionStarted(TextBox sender, TextCompositionStartedEventArgs args)
    {
        _isImeComposing = true;
    }

    private void MessageInput_TextCompositionEnded(TextBox sender, TextCompositionEndedEventArgs args)
    {
        _isImeComposing = false;
    }

    private async Task SendMessageAsync()
    {
        var body = MessageInput.Text.Trim();
        if (body.Length == 0)
        {
            return;
        }

        MessageInput.Text = string.Empty;
        try
        {
            await _hubService.SendMessageAsync(Guid.NewGuid(), body);
            MessageInput.Focus(FocusState.Programmatic);
        }
        catch (Exception exception)
        {
            MessageInput.Text = body;
            MessageInput.SelectionStart = MessageInput.Text.Length;
            MessageInput.Focus(FocusState.Programmatic);
            ShowStatus($"메시지를 보내지 못했습니다: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // My avatar with picker
        var myAvatarPath = _settingsService.AvatarPath;
        var myAvatarImage = new Ellipse
        {
            Width = 44,
            Height = 44,
            Visibility = Visibility.Collapsed
        };

        var myAvatarInitial = new TextBlock
        {
            Text = UserName[..1],
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["BrushAccent"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var myAvatarEllipse = new Ellipse
        {
            Fill = (Brush)Application.Current.Resources["BrushAccentSoft"]
        };

        if (!string.IsNullOrWhiteSpace(myAvatarPath))
        {
            try
            {
                var uri = new Uri(myAvatarPath, UriKind.Absolute);
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
                myAvatarImage.Fill = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
                myAvatarImage.Visibility = Visibility.Visible;
                myAvatarInitial.Visibility = Visibility.Collapsed;
                myAvatarEllipse.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            catch { }
        }

        var accountAvatar = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Child = new Grid
            {
                Children = { myAvatarEllipse, myAvatarImage, myAvatarInitial }
            }
        };

        var changeAvatarButton = new Button
        {
            Content = "&#xE74E;",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
            FontSize = 14,
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = (Brush)Application.Current.Resources["BrushBubbleIn"],
            Foreground = (Brush)Application.Current.Resources["BrushTextPrimary"],
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, -8, -8)
        };
        ToolTipService.SetToolTip(changeAvatarButton, "아바타 변경");
        changeAvatarButton.Click += async (_, _) =>
        {
            if (App.CurrentWindow is null) return;
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow));
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".webp");
            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                _settingsService.AvatarPath = file.Path;
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(file.Path));
                myAvatarImage.Fill = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
                myAvatarImage.Visibility = Visibility.Visible;
                myAvatarInitial.Visibility = Visibility.Collapsed;
                myAvatarEllipse.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                ApplyPartnerAvatar(file.Path); // Also update partner avatar for demo
            }
        };

        var avatarContainer = new Grid { Children = { accountAvatar, changeAvatarButton } };

        var accountText = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        accountText.Children.Add(new TextBlock
        {
            Text = UserName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["BrushTextStrong"]
        });
        accountText.Children.Add(new TextBlock
        {
            Text = "내 프로필",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["BrushTextMeta"]
        });
        var accountRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        accountRow.Children.Add(avatarContainer);
        accountRow.Children.Add(accountText);

        var notificationsToggle = new ToggleSwitch
        {
            Header = "알림",
            OnContent = "켜짐",
            OffContent = "꺼짐",
            IsOn = _settingsService.NotificationsEnabled
        };
        var userNameSelector = new ComboBox
        {
            Header = "이 기기에서 사용할 이름",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items = { "철수", "짱구" },
            SelectedItem = UserName
        };
        var downloadFolderTextBox = new TextBox
        {
            Header = "다운로드 폴더",
            Text = _settingsService.DownloadFolder,
            IsReadOnly = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var chooseFolderButton = new Button
        {
            Content = "폴더 변경",
            HorizontalAlignment = HorizontalAlignment.Left
        };
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

        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var content = new StackPanel { Spacing = 16, MinWidth = 380 };
        content.Children.Add(accountRow);
        content.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["BrushStroke"]
        });
        content.Children.Add(userNameSelector);
        content.Children.Add(notificationsToggle);
        content.Children.Add(downloadFolderTextBox);
        content.Children.Add(chooseFolderButton);
        content.Children.Add(new TextBlock
        {
            Text = $"OneToOne Messenger  {version}",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["BrushTextMeta"]
        });

        var dialog = new ContentDialog
        {
            Title = "설정",
            Content = content,
            PrimaryButtonText = "저장",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var identityChanged = userNameSelector.SelectedItem is string selectedName && selectedName != UserName;
            _settingsService.NotificationsEnabled = notificationsToggle.IsOn;
            _settingsService.DownloadFolder = downloadFolderTextBox.Text;
            if (identityChanged && userNameSelector.SelectedItem is string newUserName)
            {
                _settingsService.UserName = newUserName;
                try
                {
                    await SwitchIdentityAsync();
                }
                catch (Exception exception)
                {
                    ShowStatus($"이름은 저장했지만 연결을 전환하지 못했습니다: {exception.Message}", InfoBarSeverity.Error);
                    return;
                }
            }
            ShowStatus("설정을 저장했습니다.", InfoBarSeverity.Success);
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSearch();
    }

    private void OpenSearch()
    {
        if (_isCompactLayout)
        {
            VaultSplitView.IsPaneOpen = false;
        }

        SearchOverlay.Visibility = Visibility.Visible;
        MessageSearchInput.Focus(FocusState.Programmatic);
    }

    private void CloseSearchButton_Click(object sender, RoutedEventArgs e)
    {
        CloseSearch();
    }

    private void CloseSearch()
    {
        SearchOverlay.Visibility = Visibility.Collapsed;
        MessageSearchInput.Text = string.Empty;
        _searchResults.Clear();
        SearchStatusText.Text = string.Empty;
        MessageInput.Focus(FocusState.Programmatic);
    }

    private void SearchKeyboardAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        OpenSearch();
        args.Handled = true;
    }

    private void MainPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        if (SearchOverlay.Visibility == Visibility.Visible)
        {
            CloseSearch();
            e.Handled = true;
        }
        else if (_isCompactLayout && VaultSplitView.IsPaneOpen)
        {
            VaultSplitView.IsPaneOpen = false;
            e.Handled = true;
        }
    }

    private async void MessageSearchInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            CloseSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await SearchMessagesAsync();
        }
    }

    private async void MessageSearchButton_Click(object sender, RoutedEventArgs e)
    {
        await SearchMessagesAsync();
    }

    private async Task SearchMessagesAsync()
    {
        var query = MessageSearchInput.Text.Trim();
        _searchResults.Clear();
        if (query.Length == 0)
        {
            SearchStatusText.Text = "검색어를 입력해 주세요.";
            return;
        }

        SearchStatusText.Text = "검색 중…";
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
                : $"검색 결과 {_searchResults.Count}건";
        }
        catch (Exception exception)
        {
            SearchStatusText.Text = "검색하지 못했습니다.";
            ShowStatus($"검색에 실패했습니다: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private void SearchResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SearchResultDto result)
        {
            return;
        }

        AddOrReplaceMessage(result.Msg);
        CloseSearch();
        ScrollToMessage(result.Msg.Seq);
    }

    private async void VaultSearchInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        _vaultSearchDebounce?.Cancel();
        try
        {
            await RefreshVaultAsync();
        }
        catch (Exception exception)
        {
            ShowStatus($"파일을 검색하지 못했습니다: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async void VaultSearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_pageLoaded)
        {
            return;
        }

        _vaultSearchDebounce?.Cancel();
        var cancellation = new CancellationTokenSource();
        _vaultSearchDebounce = cancellation;
        try
        {
            await Task.Delay(350, cancellation.Token);
            await RefreshVaultAsync();
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke superseded this search.
        }
        catch (Exception exception)
        {
            ShowStatus($"파일을 검색하지 못했습니다: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async void VaultCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string category })
        {
            await SelectVaultCategoryAsync(category);
        }
    }

    private async void VaultSummaryCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string category })
        {
            await SelectVaultCategoryAsync(category);
        }
    }

    private async Task SelectVaultCategoryAsync(string category)
    {
        _vaultCategory = string.IsNullOrWhiteSpace(category) ? null : category;
        foreach (var button in GetVaultCategoryButtons())
        {
            button.IsChecked = string.Equals(button.Tag as string, category, StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            await RefreshVaultAsync();
        }
        catch (Exception exception)
        {
            ShowStatus($"보관함 필터를 적용하지 못했습니다: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private ToggleButton[] GetVaultCategoryButtons() =>
    [
        AllVaultCategoryButton,
        ImageVaultCategoryButton,
        VideoVaultCategoryButton,
        DocumentVaultCategoryButton,
        OtherVaultCategoryButton
    ];

    private async void VaultSort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (VaultSortComboBox.SelectedItem is not ComboBoxItem { Tag: string sort })
        {
            return;
        }

        _vaultSort = sort;
        if (!_pageLoaded)
        {
            return;
        }

        try
        {
            await RefreshVaultAsync();
        }
        catch (Exception exception)
        {
            ShowStatus($"정렬을 적용하지 못했습니다: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async void RefreshVaultButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshVaultAsync(showFeedback: true);
        }
        catch (Exception exception)
        {
            ShowStatus($"보관함을 새로 고치지 못했습니다: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    // ─────────────────────────────────────────────────────────
    //  채팅 배경 원 패턴 (텔레그램 스타일)
    // ─────────────────────────────────────────────────────────

    private void PatternCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawCirclePattern((Canvas)sender, e.NewSize.Width, e.NewSize.Height);
    }

    private static void DrawCirclePattern(Canvas canvas, double width, double height)
    {
        canvas.Children.Clear();

        const double spacing = 42;   // 원 간격 (px)
        const double radius  = 4.5;  // 원 반지름 (px)
        const byte   alpha   = 38;   // 투명도 (0~255)

        // 텔레그램 기본 채팅 캔버스 (#DAE7F3) 위에 더 짙은 파란빛 원
        var fill = new SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(alpha, 0x72, 0x9A, 0xBD));

        // 오프셋 행마다 절반씩 이동해서 벌집 패턴 느낌
        var row = 0;
        for (double y = radius; y < height + spacing; y += spacing, row++)
        {
            var offsetX = (row % 2 == 0) ? 0 : spacing / 2.0;
            for (double x = offsetX + radius; x < width + spacing; x += spacing)
            {
                var ellipse = new Ellipse
                {
                    Width  = radius * 2,
                    Height = radius * 2,
                    Fill   = fill
                };
                Canvas.SetLeft(ellipse, x - radius);
                Canvas.SetTop(ellipse,  y - radius);
                canvas.Children.Add(ellipse);
            }
        }
    }

    private void MainPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < CompactLayoutThreshold;
        if (compact == _isCompactLayout && VaultSplitView.DisplayMode == (compact ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline))
        {
            return;
        }

        _isCompactLayout = compact;
        VaultSplitView.DisplayMode = compact
            ? SplitViewDisplayMode.Overlay
            : SplitViewDisplayMode.Inline;
        VaultSplitView.IsPaneOpen = compact ? false : _wideVaultPreference;
        UpdateVaultToggleVisual();
    }

    private void VaultToggleButton_Click(object sender, RoutedEventArgs e)
    {
        VaultSplitView.IsPaneOpen = !VaultSplitView.IsPaneOpen;
        if (!_isCompactLayout)
        {
            _wideVaultPreference = VaultSplitView.IsPaneOpen;
        }

        UpdateVaultToggleVisual();
    }

    private void CloseVaultButton_Click(object sender, RoutedEventArgs e)
    {
        VaultSplitView.IsPaneOpen = false;
        if (!_isCompactLayout)
        {
            _wideVaultPreference = false;
        }

        UpdateVaultToggleVisual();
    }

    private void UpdateVaultToggleVisual()
    {
        VaultToggleButton.Background = VaultSplitView.IsPaneOpen
            ? (Brush)Application.Current.Resources["BrushAccentSoft"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        VaultToggleButton.Foreground = VaultSplitView.IsPaneOpen
            ? (Brush)Application.Current.Resources["BrushAccent"]
            : (Brush)Application.Current.Resources["BrushTextPrimary"];
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
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
        ShowTransfer($"{file.Name} 전송 중…", showProgress: true);
        UploadProgress.Value = 0;
        using var cancellation = new CancellationTokenSource();
        _activeUploadCancellation = cancellation;
        try
        {
            if (!_uploadMessageIds.TryGetValue(file.Path, out var clientMessageId))
            {
                clientMessageId = Guid.NewGuid();
                _uploadMessageIds[file.Path] = clientMessageId;
            }
            var progress = new Progress<double>(value =>
            {
                UploadProgress.Value = value * 100;
                TransferStatusText.Text = $"{file.Name} 전송 중 · {value:P0}";
            });
            var message = await _apiService.UploadFileAsync(file, UserName, clientMessageId, progress, cancellation.Token);
            AddOrReplaceMessage(message);
            ScrollToMessage(message.Seq, highlight: false);
            await RefreshVaultAsync();
            _failedFiles.RemoveAll(item => item.Path == file.Path);
            _uploadMessageIds.Remove(file.Path);
            ShowStatus($"{file.Name} 파일을 보냈습니다.", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _uploadMessageIds.Remove(file.Path);
            TransferStatusText.Text = $"{file.Name} 전송 취소됨";
            ShowStatus($"{file.Name} 전송을 취소했습니다.", InfoBarSeverity.Informational);
        }
        catch (Exception exception)
        {
            if (_failedFiles.All(item => item.Path != file.Path))
            {
                _failedFiles.Add(file);
            }

            var friendlyMessage = FormatFileErrorMessage(exception);
            TransferStatusText.Text = $"{file.Name} 전송 실패";
            ShowStatus($"파일을 보내지 못했습니다: {friendlyMessage}", InfoBarSeverity.Error);
        }
        finally
        {
            if (ReferenceEquals(_activeUploadCancellation, cancellation))
            {
                _activeUploadCancellation = null;
            }
            UpdateTransferFailureState();
        }
    }

    private void CancelTransferButton_Click(object sender, RoutedEventArgs e)
    {
        _activeUploadCancellation?.Cancel();
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        var pending = _failedFiles.ToArray();
        foreach (var file in pending)
        {
            await UploadFileAsync(file);
        }
    }

    private void ShowTransfer(string text, bool showProgress)
    {
        TransferStatusText.Text = text;
        TransferStatusPanel.Visibility = Visibility.Visible;
        UploadProgress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;
        CancelTransferButton.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTransferFailureState()
    {
        if (_failedFiles.Count == 0)
        {
            TransferStatusPanel.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Collapsed;
            CancelTransferButton.Visibility = Visibility.Collapsed;
            return;
        }

        TransferStatusPanel.Visibility = Visibility.Visible;
        UploadProgress.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Visible;
        CancelTransferButton.Visibility = Visibility.Collapsed;
        TransferStatusText.Text = $"전송하지 못한 파일 {_failedFiles.Count}개";
    }

    private void ChatArea_DragEnter(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    private void ChatArea_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void ChatArea_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            return;
        }

        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "파일 전송";
        e.DragUIOverride.IsContentVisible = true;
    }

    private async void ChatArea_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
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

    private async void PreviewImage_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not Image { Tag: Guid fileId })
        {
            return;
        }

        var image = new Image
        {
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(_apiService.BaseAddress, $"api/files/{fileId}/download")),
            MaxWidth = 960,
            MaxHeight = 680,
            Stretch = Stretch.Uniform
        };
        var previewSurface = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 21, 25, 33)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Child = image
        };
        var dialog = new ContentDialog
        {
            Title = "이미지 미리보기",
            CloseButtonText = "닫기",
            XamlRoot = Content.XamlRoot,
            Content = previewSurface
        };
        await dialog.ShowAsync();
    }

    private void PreviewImage_Failed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is not Image image || image.Parent is not Grid previewContainer)
        {
            return;
        }

        image.Visibility = Visibility.Collapsed;
        if (previewContainer.Children.Count > 1 &&
            previewContainer.Children[1] is FrameworkElement placeholder)
        {
            placeholder.Visibility = Visibility.Visible;
        }
    }

    private async void DownloadFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: object tag } || GetFileDto(tag) is not { } file)
        {
            return;
        }

        try
        {
            var folderPath = _settingsService.DownloadFolder;
            Directory.CreateDirectory(folderPath);
            var destinationPath = GetAvailablePath(folderPath, file.OriginalName);
            await DownloadToPathAsync(file, destinationPath);
            ShowStatus($"다운로드 완료 · {file.OriginalName}", InfoBarSeverity.Success);
            _notificationService.TryShow("다운로드 완료", file.OriginalName, out _);
        }
        catch (Exception exception)
        {
            ShowStatus($"다운로드하지 못했습니다: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async void SaveAsFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: object tag } ||
            GetFileDto(tag) is not { } file ||
            App.CurrentWindow is null)
        {
            return;
        }

        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow));
            var originalName = System.IO.Path.GetFileName(file.OriginalName);
            var extension = System.IO.Path.GetExtension(originalName);
            picker.SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(originalName);
            picker.FileTypeChoices.Add(
                string.IsNullOrWhiteSpace(extension) ? "파일" : extension.ToUpperInvariant() + " 파일",
                [string.IsNullOrWhiteSpace(extension) ? ".bin" : extension]);

            var destination = await picker.PickSaveFileAsync();
            if (destination is null)
            {
                return;
            }

            await DownloadToPathAsync(file, destination.Path);
            ShowStatus($"저장 완료 · {file.OriginalName}", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus($"파일을 저장하지 못했습니다: {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private static FileDto? GetFileDto(object tag) =>
        tag switch
        {
            FileDto file => file,
            VaultFileDto file => new FileDto(
                file.Id,
                file.OriginalName,
                file.MimeType,
                file.SizeBytes,
                file.Category,
                file.CreatedAt),
            _ => null
        };

    private async Task DownloadToPathAsync(FileDto file, string destinationPath)
    {
        ShowTransfer($"{file.OriginalName} 다운로드 중…", showProgress: true);
        UploadProgress.Value = 0;
        var progress = new Progress<double>(value =>
        {
            UploadProgress.Value = value * 100;
            TransferStatusText.Text = $"{file.OriginalName} 다운로드 중 · {value:P0}";
        });

        try
        {
            await _apiService.DownloadFileAsync(file.Id, destinationPath, progress);
        }
        finally
        {
            UpdateTransferFailureState();
        }
    }

    private static string GetAvailablePath(string folderPath, string originalName)
    {
        var safeName = System.IO.Path.GetFileName(originalName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "download";
        }

        var path = System.IO.Path.Combine(folderPath, safeName);
        if (!File.Exists(path))
        {
            return path;
        }

        var name = System.IO.Path.GetFileNameWithoutExtension(safeName);
        var extension = System.IO.Path.GetExtension(safeName);
        for (var index = 1; ; index++)
        {
            path = System.IO.Path.Combine(folderPath, $"{name} ({index}){extension}");
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }

    private void ShowStatus(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        OperationInfoBar.Message = message;
        OperationInfoBar.Severity = severity;
        OperationInfoBar.IsOpen = true;

        if (_statusTimer is null)
        {
            _statusTimer = DispatcherQueue.CreateTimer();
            _statusTimer.Interval = TimeSpan.FromSeconds(4);
            _statusTimer.IsRepeating = false;
            _statusTimer.Tick += (_, _) => OperationInfoBar.IsOpen = false;
        }

        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private static string FormatFileErrorMessage(Exception exception)
    {
        if ((uint)exception.HResult == 0x8007016A ||
            exception.Message.Contains("클라우드 파일 공급자", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("cloud file", StringComparison.OrdinalIgnoreCase) ||
            (exception.InnerException is not null && ((uint)exception.InnerException.HResult == 0x8007016A ||
             exception.InnerException.Message.Contains("클라우드 파일 공급자", StringComparison.OrdinalIgnoreCase))))
        {
            return "클라우드(OneDrive 등) 동기화 파일 공급자가 실행되고 있지 않아 파일을 읽을 수 없습니다. OneDrive를 실행하거나 로컬 일반 폴더의 파일을 선택해 주세요.";
        }

        if (exception is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge)
            {
                return "파일 크기가 서버 허용 한도(최대 512MB)를 초과했습니다.";
            }

            if (!string.IsNullOrWhiteSpace(httpEx.Message))
            {
                return httpEx.Message;
            }
        }

        if (!string.IsNullOrWhiteSpace(exception.Message))
        {
            return exception.Message;
        }

        if (exception.InnerException is not null && !string.IsNullOrWhiteSpace(exception.InnerException.Message))
        {
            return exception.InnerException.Message;
        }

        return $"알 수 없는 오류가 발생했습니다 (0x{exception.HResult:X8})";
    }
}

public sealed class ChatMessageItem
{
    public ChatMessageItem(
        MessageDto message,
        bool isMine,
        bool showDateHeader,
        string dateHeader,
        bool showAvatar,
        bool showSender,
        Thickness groupMargin,
        string? avatarPath = null,
        string partnerInitial = "짱")
    {
        Message = message;
        IsMine = isMine;
        ShowDateHeader = showDateHeader;
        DateHeader = dateHeader;
        ShowAvatar = showAvatar;
        ShowSender = showSender;
        GroupMargin = groupMargin;
        AvatarPath = avatarPath;
        PartnerInitial = partnerInitial;

        // 꼬리는 그룹의 마지막 메시지(ShowAvatar=true)에만 표시
        ShowTailIncoming = !isMine && showAvatar;
        ShowTailOutgoing = isMine && showAvatar;

        // 파일 전용 메시지: Body가 없고 이미지 아닌 파일 첨부 → 버블 없이 카드만 표시
        IsFileOnly = message.File is not null
                  && string.IsNullOrWhiteSpace(message.Body)
                  && !string.Equals(message.File.Category, "image", StringComparison.OrdinalIgnoreCase);
    }

    public MessageDto Message { get; }
    public bool IsMine { get; }
    public bool ShowDateHeader { get; }
    public string DateHeader { get; }
    public bool ShowAvatar { get; }
    public bool ShowSender { get; }
    public Thickness GroupMargin { get; }
    public string? AvatarPath { get; }
    public string PartnerInitial { get; }
    public bool ShowTailIncoming { get; }
    public bool ShowTailOutgoing { get; }
    public bool IsFileOnly { get; }
    /// <summary>버블 표시 여부 (파일 전용이 아닐 때만 true)</summary>
    public bool ShowBubble => !IsFileOnly;
}
