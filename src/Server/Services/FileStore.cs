using Microsoft.AspNetCore.StaticFiles;

namespace MessengerServer.Services;

public sealed class FileStore
{
    private readonly string _rootPath;
    private readonly FileExtensionContentTypeProvider _contentTypes = new();

    public FileStore(IHostEnvironment environment)
    {
        _rootPath = Path.Combine(environment.ContentRootPath, "data", "files");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<StoredFile> SaveAsync(
        Guid fileId,
        string originalName,
        Stream content,
        long? expectedLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalName);

        var safeName = Path.GetFileName(originalName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName is "." or "..")
        {
            throw new ArgumentException("A valid file name is required.", nameof(originalName));
        }

        var now = DateTimeOffset.UtcNow;
        var directory = Path.Combine(_rootPath, now.ToString("yyyy"), now.ToString("MM"));
        Directory.CreateDirectory(directory);

        var storedName = $"{fileId:N}_{safeName}";
        var fullPath = Path.Combine(directory, storedName);
        await using var output = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);

        await content.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);

        var size = output.Length;
        if (expectedLength.HasValue && expectedLength.Value != size)
        {
            File.Delete(fullPath);
            throw new InvalidDataException("Uploaded content length does not match Content-Length.");
        }

        var mimeType = _contentTypes.TryGetContentType(safeName, out var detectedType)
            ? detectedType
            : "application/octet-stream";

        return new StoredFile(
            fileId,
            safeName,
            mimeType,
            size,
            Path.GetRelativePath(_rootPath, fullPath),
            fullPath,
            now);
    }

    public FileStream OpenRead(string storedPath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, storedPath));
        var rootWithSeparator = Path.GetFullPath(_rootPath)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The requested file path is invalid.");
        }

        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
    }

    public void Delete(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
}

public sealed record StoredFile(
    Guid Id,
    string OriginalName,
    string MimeType,
    long SizeBytes,
    string StoredPath,
    string FullPath,
    DateTimeOffset CreatedAt);
