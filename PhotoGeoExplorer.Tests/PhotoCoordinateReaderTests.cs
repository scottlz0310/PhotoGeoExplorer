using System;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Models;
using PhotoGeoExplorer.Services;
using PhotoGeoExplorer.Services.AreaTree;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class PhotoCoordinateReaderTests
{
    [Fact]
    public async Task ReadAsyncReturnsTheLocationOfThePhoto()
    {
        var reader = new PhotoCoordinateReader(new FakeExifMetadataService(new PhotoMetadata(null, null, null, 35.49, 134.22, hasGpsData: true)));

        var coordinate = await reader.ReadAsync("/p/a.jpg", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(new PhotoCoordinate(35.49, 134.22), coordinate);
    }

    [Theory]
    // 位置情報なし
    [InlineData(null, null, false)]
    // 緯度だけ
    [InlineData(35.49, null, true)]
    // GPS はあるが測位に失敗した（0, 0）
    [InlineData(0.0, 0.0, true)]
    public async Task ReadAsyncReturnsNothingWithoutAValidLocation(double? latitude, double? longitude, bool hasGpsData)
    {
        var reader = new PhotoCoordinateReader(new FakeExifMetadataService(new PhotoMetadata(null, null, null, latitude, longitude, hasGpsData)));

        Assert.Null(await reader.ReadAsync("/p/a.jpg", CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task ReadAsyncReturnsNothingWhenTheMetadataCannotBeRead()
    {
        var reader = new PhotoCoordinateReader(new FakeExifMetadataService(null));

        Assert.Null(await reader.ReadAsync("/p/a.jpg", CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public void ConstructorRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new PhotoCoordinateReader(null!));
    }

    [Fact]
    public async Task ReadAsyncRejectsAnEmptyPath()
    {
        var reader = new PhotoCoordinateReader(new FakeExifMetadataService(null));

        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync(string.Empty, CancellationToken.None)).ConfigureAwait(true);
    }

    private sealed class FakeExifMetadataService(PhotoMetadata? metadata) : IExifMetadataService
    {
        public Task<PhotoMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken) => Task.FromResult(metadata);

        public Task<bool> UpdateMetadataAsync(
            string filePath,
            DateTimeOffset? takenAt,
            double? latitude,
            double? longitude,
            bool updateFileModifiedDate,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
