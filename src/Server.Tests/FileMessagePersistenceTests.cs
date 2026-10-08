using MessengerServer.Data;
using MessengerServer.Services;
using Microsoft.Extensions.Hosting;
using Shared;
using Xunit;

namespace Server.Tests;

public sealed class FileMessagePersistenceTests
{
    [Fact]
    public async Task SaveAsync_RemovesTemporaryFileWhenValidationFails()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "OneToOneMessengerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var fileStore = new FileStore(new TestHostEnvironment(contentRoot));
            await using var content = new MemoryStream([1, 2, 3]);

            await Assert.ThrowsAsync<InvalidDataException>(() => fileStore.SaveAsync(
                Guid.NewGuid(),
                "sample.txt",
                content,
                expectedLength: 4));

            var filesRoot = Path.Combine(contentRoot, "data", "files");
            Assert.Empty(Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsFileMetadataForPersistedFileMessage()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "OneToOneMessengerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var database = new DatabaseInitializer(new TestHostEnvironment(contentRoot));
            database.Initialize();
            var store = new MessageStore(database);
            var fileId = Guid.NewGuid();
            var messageId = Guid.NewGuid();
            var createdAt = DateTimeOffset.UtcNow;

            await store.AddFileMessageAsync(
                messageId,
                new StoredFile(
                    fileId,
                    "전송한-파일.pdf",
                    "application/pdf",
                    1234,
                    "2026/10/file.pdf",
                    Path.Combine(contentRoot, "file.pdf"),
                    createdAt),
                "철수",
                "doc",
                null);

            var message = Assert.Single(await store.GetRecentAsync());
            Assert.Equal(MsgType.File, message.Type);
            Assert.NotNull(message.File);
            Assert.Equal(fileId, message.File!.Id);
            Assert.Equal("전송한-파일.pdf", message.File.OriginalName);
            Assert.Equal(1234, message.File.SizeBytes);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetMessageAsync_ReturnsExistingFileMessageForRetry()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "OneToOneMessengerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var database = new DatabaseInitializer(new TestHostEnvironment(contentRoot));
            database.Initialize();
            var store = new MessageStore(database);
            var messageId = Guid.NewGuid();
            var fileId = Guid.NewGuid();
            var createdAt = DateTimeOffset.UtcNow;
            await store.AddFileMessageAsync(
                messageId,
                new StoredFile(fileId, "retry.txt", "text/plain", 10, "2026/10/retry.txt", "unused", createdAt),
                "철수",
                "doc",
                null);

            var existing = await store.GetMessageAsync(messageId);

            Assert.NotNull(existing);
            Assert.Equal(messageId, existing!.Id);
            Assert.Equal(fileId, existing.File!.Id);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task MarkReadAsync_OnlyMarksMessagesSentByTheOtherUser()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "OneToOneMessengerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var database = new DatabaseInitializer(new TestHostEnvironment(contentRoot));
            database.Initialize();
            var store = new MessageStore(database);
            await store.AddTextMessageAsync(Guid.NewGuid(), "철수", "읽음 확인", DateTimeOffset.UtcNow);
            var sequence = Assert.Single(await store.GetRecentAsync()).Seq;

            Assert.Empty(await store.MarkReadAsync("철수", [sequence], DateTimeOffset.UtcNow));
            Assert.Equal([sequence], await store.MarkReadAsync("짱구", [sequence], DateTimeOffset.UtcNow));

            var message = Assert.Single(await store.GetRecentAsync());
            Assert.True(message.IsRead);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Server.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
