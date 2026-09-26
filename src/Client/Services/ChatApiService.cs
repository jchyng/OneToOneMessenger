using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net;
using Shared;
using Windows.Storage;

namespace OneToOneMessenger_Client.Services;

public sealed class ChatApiService
{
    private readonly HttpClient _httpClient;

    public ChatApiService(string baseAddress = "http://localhost:5000")
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseAddress, UriKind.Absolute)
        };
    }

    public Task<IReadOnlyList<MessageDto>?> GetMessagesAsync(
        int limit = 50,
        long? beforeSeq = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"api/messages?limit={limit}";
        if (beforeSeq.HasValue)
        {
            query += $"&beforeSeq={beforeSeq.Value}";
        }

        return _httpClient.GetFromJsonAsync<IReadOnlyList<MessageDto>>(query, cancellationToken);
    }

    public Task<IReadOnlyList<SearchResultDto>?> SearchMessagesAsync(
        string query,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<SearchResultDto>?>(Array.Empty<SearchResultDto>());
        }

        return _httpClient.GetFromJsonAsync<IReadOnlyList<SearchResultDto>>(
            $"api/messages/search?q={Uri.EscapeDataString(query.Trim())}&limit={limit}",
            cancellationToken);
    }

    public Task<IReadOnlyList<VaultFileDto>?> GetVaultAsync(
        string? category = null,
        string? query = null,
        string sort = "newest",
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>
        {
            $"sort={Uri.EscapeDataString(sort)}",
            $"offset={offset}",
            $"limit={limit}"
        };
        if (!string.IsNullOrWhiteSpace(category))
        {
            parameters.Add($"category={Uri.EscapeDataString(category)}");
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            parameters.Add($"q={Uri.EscapeDataString(query)}");
        }

        return _httpClient.GetFromJsonAsync<IReadOnlyList<VaultFileDto>>(
            $"api/vault?{string.Join("&", parameters)}",
            cancellationToken);
    }

    public Task<VaultSummaryDto?> GetVaultSummaryAsync(CancellationToken cancellationToken = default)
    {
        return _httpClient.GetFromJsonAsync<VaultSummaryDto>("api/vault/summary", cancellationToken);
    }

    public async Task<MessageDto> UploadFileAsync(
        StorageFile file,
        string sender,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using var stream = await file.OpenStreamForReadAsync();
        using var content = new ProgressStreamContent(stream, progress);
        content.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);

        var uri = $"api/files/upload?name={Uri.EscapeDataString(file.Name)}&sender={Uri.EscapeDataString(sender)}";
        using var response = await _httpClient.PostAsync(uri, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MessageDto>(cancellationToken: cancellationToken))!;
    }

    public async Task DownloadFileAsync(
        Guid fileId,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"api/files/{fileId}/download",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true);

        var total = response.Content.Headers.ContentLength;
        var buffer = new byte[64 * 1024];
        long copied = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
            if (total is > 0)
            {
                progress?.Report((double)copied / total.Value);
            }
        }
    }

    private sealed class ProgressStreamContent : HttpContent
    {
        private readonly Stream _stream;
        private readonly IProgress<double>? _progress;

        public ProgressStreamContent(Stream stream, IProgress<double>? progress)
        {
            _stream = stream;
            _progress = progress;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = new byte[64 * 1024];
            var total = _stream.CanSeek ? _stream.Length : -1;
            long copied = 0;
            int read;
            while ((read = await _stream.ReadAsync(buffer)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read));
                copied += read;
                if (total > 0)
                {
                    _progress?.Report((double)copied / total);
                }
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            if (_stream.CanSeek)
            {
                length = _stream.Length;
                return true;
            }

            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stream.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
