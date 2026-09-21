using AiDetection.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiDetection.Tests;

public sealed class ImageClassifierTests
{
    [Theory]
    [InlineData(100, 200)]
    [InlineData(200, 100)]
    [InlineData(384, 384)]
    public void Published_preprocessing_produces_384_square_crop(int width, int height)
    {
        using var source = new Image<Rgb24>(width, height);
        using var prepared = ImageClassifier.PrepareImage(source);
        Assert.Equal(384, prepared.Width);
        Assert.Equal(384, prepared.Height);
    }
}
