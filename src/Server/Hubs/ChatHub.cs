using Microsoft.AspNetCore.SignalR;
using MessengerServer.Services;

namespace MessengerServer.Hubs;

public sealed class ChatHub : Hub
{
    private readonly MessageStore _messageStore;

    public ChatHub(MessageStore messageStore)
    {
        _messageStore = messageStore;
    }

    public override async Task OnConnectedAsync()
    {
        var user = Context.GetHttpContext()?.Request.Query["user"].ToString();
        if (string.IsNullOrWhiteSpace(user))
        {
            Context.Abort();
            return;
        }

        Context.Items["user"] = user;
        await Clients.Others.SendAsync("PeerPresence", true);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Clients.Others.SendAsync("PeerPresence", false);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendMessage(string clientMsgId, string body)
    {
        var sender = Context.Items["user"]?.ToString();
        if (string.IsNullOrWhiteSpace(sender))
        {
            throw new HubException("The connection has no user identity.");
        }

        if (!Guid.TryParse(clientMsgId, out var messageId))
        {
            throw new HubException("clientMsgId must be a valid GUID.");
        }

        var message = await _messageStore.AddTextMessageAsync(
            messageId,
            sender,
            body,
            DateTimeOffset.UtcNow,
            Context.ConnectionAborted);

        await Clients.All.SendAsync("MessageReceived", message, Context.ConnectionAborted);
    }

    public async Task MarkRead(long[] seqList)
    {
        var reader = Context.Items["user"]?.ToString();
        if (string.IsNullOrWhiteSpace(reader))
        {
            throw new HubException("The connection has no user identity.");
        }

        var readAt = DateTimeOffset.UtcNow;
        var updated = await _messageStore.MarkReadAsync(seqList, readAt, Context.ConnectionAborted);
        if (updated.Count > 0)
        {
            await Clients.All.SendAsync("MessagesRead", reader, updated, readAt, Context.ConnectionAborted);
        }
    }
}
