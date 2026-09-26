using Microsoft.AspNetCore.SignalR;
using MessengerServer.Data;
using MessengerServer.Hubs;
using MessengerServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://localhost:5000");
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton<FileCategoryService>();
builder.Services.AddSingleton<FileStore>();
builder.Services.AddSingleton<MessageStore>();
builder.Services.AddSignalR();

var app = builder.Build();
app.Services.GetRequiredService<DatabaseInitializer>().Initialize();

app.MapGet("/api/health", () => Results.Ok(new
{
    Status = "ok",
    Service = "MessengerServer",
    Utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/messages", async (
    int? beforeSeq,
    int? limit,
    MessageStore messageStore,
    CancellationToken cancellationToken) =>
{
    var effectiveLimit = limit ?? 50;
    if (effectiveLimit is < 1 or > 100)
    {
        return Results.BadRequest(new { Error = "limit must be between 1 and 100." });
    }

    var messages = await messageStore.GetRecentAsync(
        effectiveLimit,
        beforeSeq,
        cancellationToken);
    return Results.Ok(messages);
});

app.MapGet("/api/messages/search", async (
    string? q,
    int? limit,
    MessageStore messageStore,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.BadRequest(new { Error = "q is required." });
    }

    var effectiveLimit = limit ?? 100;
    if (effectiveLimit is < 1 or > 100)
    {
        return Results.BadRequest(new { Error = "limit must be between 1 and 100." });
    }

    var results = await messageStore.SearchAsync(q, effectiveLimit, cancellationToken);
    return Results.Ok(results);
});

app.MapPost("/api/files/upload", async (
    HttpRequest request,
    string? name,
    string? sender,
    string? body,
    FileStore fileStore,
    FileCategoryService categoryService,
    MessageStore messageStore,
    IHubContext<ChatHub> hub,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(sender))
    {
        return Results.BadRequest(new { Error = "name and sender are required." });
    }

    if (request.ContentLength is 0)
    {
        return Results.BadRequest(new { Error = "A non-empty request body is required." });
    }

    var fileId = Guid.NewGuid();
    var messageId = Guid.NewGuid();
    StoredFile? storedFile = null;
    try
    {
        storedFile = await fileStore.SaveAsync(
            fileId,
            name,
            request.Body,
            request.ContentLength,
            cancellationToken);
        var category = categoryService.Classify(storedFile.OriginalName);
        var message = await messageStore.AddFileMessageAsync(
            messageId,
            storedFile,
            sender,
            category,
            body,
            cancellationToken);
        await hub.Clients.All.SendAsync("MessageReceived", message, cancellationToken);
        return Results.Ok(message);
    }
    catch
    {
        if (storedFile is not null)
        {
            fileStore.Delete(storedFile.FullPath);
        }

        throw;
    }
});

app.MapGet("/api/files/{id:guid}/download", async (
    Guid id,
    FileStore fileStore,
    MessageStore messageStore,
    CancellationToken cancellationToken) =>
{
    var file = await messageStore.GetFileAsync(id, cancellationToken);
    if (file is null)
    {
        return Results.NotFound();
    }

    var stream = fileStore.OpenRead(file.StoredPath);
    return Results.File(stream, file.MimeType, file.OriginalName, enableRangeProcessing: true);
});

app.MapGet("/api/vault", async (
    string? category,
    string? q,
    string? sort,
    int? offset,
    int? limit,
    MessageStore messageStore,
    CancellationToken cancellationToken) =>
{
    var effectiveOffset = offset ?? 0;
    var effectiveLimit = limit ?? 20;
    if (effectiveOffset < 0 || effectiveLimit is < 1 or > 100)
    {
        return Results.BadRequest(new { Error = "offset must be non-negative and limit must be between 1 and 100." });
    }

    try
    {
        var files = await messageStore.GetVaultAsync(
            category,
            q,
            sort ?? "newest",
            effectiveOffset,
            effectiveLimit,
            cancellationToken);
        return Results.Ok(files);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { Error = exception.Message });
    }
});

app.MapGet("/api/vault/summary", async (
    MessageStore messageStore,
    CancellationToken cancellationToken) =>
{
    var summary = await messageStore.GetVaultSummaryAsync(cancellationToken);
    return Results.Ok(summary);
});

app.MapHub<ChatHub>("/hub/chat");

app.Run();
