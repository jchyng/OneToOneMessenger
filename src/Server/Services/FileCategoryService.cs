namespace MessengerServer.Services;

public sealed class FileCategoryService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".mkv", ".webm"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".md", ".csv", ".hwp"
    };

    public string Classify(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var extension = Path.GetExtension(fileName);
        if (ImageExtensions.Contains(extension))
        {
            return "image";
        }

        if (VideoExtensions.Contains(extension))
        {
            return "video";
        }

        if (DocumentExtensions.Contains(extension))
        {
            return "doc";
        }

        return "etc";
    }
}
