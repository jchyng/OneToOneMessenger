namespace Shared;

public enum MsgType
{
    Text = 0,
    File = 1,
    Image = 2
}

public sealed record MessageDto(
    long Seq,
    Guid Id,
    MsgType Type,
    string Sender,
    string? Body,
    FileDto? File,
    DateTimeOffset SentAt,
    bool IsRead,
    DateTimeOffset? ReadAt);

public sealed record FileDto(
    Guid Id,
    string OriginalName,
    string MimeType,
    long SizeBytes,
    string Category,
    DateTimeOffset CreatedAt);

public sealed record VaultSummaryDto(
    int Images,
    int Videos,
    int Docs,
    int Etc);

public sealed record VaultFileDto(
    Guid Id,
    Guid MessageId,
    long MessageSeq,
    string OriginalName,
    string MimeType,
    long SizeBytes,
    string Category,
    DateTimeOffset CreatedAt);

public sealed record SearchResultDto(
    MessageDto Msg,
    string HighlightedPreview);
