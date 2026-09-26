using Microsoft.AspNetCore.SignalR.Client;
using Shared;

namespace OneToOneMessenger_Client.Services;

public sealed class ChatHubService : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public ChatHubService(string userName, string baseAddress = "http://localhost:5000")
    {
        var hubUri = $"{baseAddress.TrimEnd('/')}/hub/chat?user={Uri.EscapeDataString(userName)}";
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUri)
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .Build();

        _connection.On<MessageDto>("MessageReceived", message => MessageReceived?.Invoke(message));
        _connection.On<string, long[], DateTimeOffset>(
            "MessagesRead",
            (reader, sequences, readAt) => MessagesRead?.Invoke(reader, sequences, readAt));
        _connection.On<bool>("PeerPresence", online => PeerPresenceChanged?.Invoke(online));
        _connection.Reconnecting += error =>
        {
            ConnectionStateChanged?.Invoke("reconnecting");
            return Task.CompletedTask;
        };
        _connection.Reconnected += _ =>
        {
            ConnectionStateChanged?.Invoke("connected");
            return Task.CompletedTask;
        };
        _connection.Closed += error =>
        {
            ConnectionStateChanged?.Invoke("disconnected");
            return Task.CompletedTask;
        };
    }

    public event Action<MessageDto>? MessageReceived;
    public event Action<string, long[], DateTimeOffset>? MessagesRead;
    public event Action<bool>? PeerPresenceChanged;
    public event Action<string>? ConnectionStateChanged;

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _connection.StartAsync(cancellationToken);

    public Task SendMessageAsync(Guid clientMessageId, string body, CancellationToken cancellationToken = default) =>
        _connection.InvokeAsync("SendMessage", clientMessageId.ToString(), body, cancellationToken);

    public Task MarkReadAsync(IEnumerable<long> sequences, CancellationToken cancellationToken = default) =>
        _connection.InvokeAsync("MarkRead", sequences.ToArray(), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
