using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MessengerServer.Data;
using MessengerServer.Hubs;
using MessengerServer.Services;
using Shared;
using Xunit;

namespace Server.Tests;

public sealed class ChatHubIntegrationTests
{
    [Fact]
    public async Task TwoClientsExchangeMessagePresenceAndReadState()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "OneToOneMessengerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRoot,
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<DatabaseInitializer>();
        builder.Services.AddSingleton<MessageStore>();
        builder.Services.AddSignalR();

        var app = builder.Build();
        try
        {
            var initializer = app.Services.GetRequiredService<DatabaseInitializer>();
            initializer.Initialize();
            app.MapHub<ChatHub>("/hub/chat");
            await app.StartAsync();

            var server = app.GetTestServer();
            var presence = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var received = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            var read = new TaskCompletionSource<(string Reader, long[] Sequences)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            await using var first = CreateConnection(server, "철수");
            first.On<bool>("PeerPresence", value => presence.TrySetResult(value));

            await using var second = CreateConnection(server, "짱구");
            second.On<MessageDto>("MessageReceived", message => received.TrySetResult(message));
            second.On<string, long[], DateTimeOffset>(
                "MessagesRead",
                (reader, sequences, _) => read.TrySetResult((reader, sequences)));

            await first.StartAsync();
            await second.StartAsync();

            Assert.True(await presence.Task.WaitAsync(TimeSpan.FromSeconds(5)));

            var messageId = Guid.NewGuid();
            await first.InvokeAsync("SendMessage", messageId.ToString(), "통합 테스트 메시지");
            var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(messageId, message.Id);
            Assert.Equal("철수", message.Sender);
            Assert.Equal("통합 테스트 메시지", message.Body);

            await second.InvokeAsync("MarkRead", new[] { message.Seq });
            var readEvent = await read.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("짱구", readEvent.Reader);
            Assert.Contains(message.Seq, readEvent.Sequences);

            await first.StopAsync();
            await second.StopAsync();
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static HubConnection CreateConnection(TestServer server, string user)
    {
        return new HubConnectionBuilder()
            .WithUrl(
                $"http://localhost/hub/chat?user={Uri.EscapeDataString(user)}",
                options => options.HttpMessageHandlerFactory = _ => server.CreateHandler())
            .Build();
    }
}
