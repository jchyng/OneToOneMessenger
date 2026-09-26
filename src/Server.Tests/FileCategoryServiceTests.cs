using Xunit;
using MessengerServer.Services;

namespace Server.Tests;

public sealed class FileCategoryServiceTests
{
    private readonly FileCategoryService _service = new();

    [Theory]
    [InlineData("photo.JPG", "image")]
    [InlineData("clip.webm", "video")]
    [InlineData("report.PDF", "doc")]
    [InlineData("archive.zip", "etc")]
    public void Classify_ReturnsExpectedCategory(string fileName, string expected)
    {
        Assert.Equal(expected, _service.Classify(fileName));
    }

    [Fact]
    public void Classify_RejectsMissingFileName()
    {
        Assert.Throws<ArgumentException>(() => _service.Classify(" "));
    }
}
