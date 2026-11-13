using System.Net;
using PhotoGeoExplorer.Services.RoadNames;
using PhotoMapStudio.Tiles;
using PhotoMapStudio.Tiles.Vector;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public class VectorRoadTileSourceTests
{
    [Fact]
    public async Task TheTileAtTheRoadZoomIsRequestedAndDecoded()
    {
        var source = new FakeVectorTileSource { Bytes = RoadLayerTile("transportation_name") };

        var tile = await new VectorRoadTileSource(source).GetTileAsync(14370, 6489, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal((14, 14370, 6489), Assert.Single(source.Requests));
        Assert.Equal("transportation_name", Assert.Single(tile!.Layers).Name);
    }

    [Fact]
    public async Task ANullTileMeansThereIsNoTile()
    {
        var source = new FakeVectorTileSource { Bytes = null };

        Assert.Null(await new VectorRoadTileSource(source).GetTileAsync(1, 2, CancellationToken.None).ConfigureAwait(true));
    }

    [Fact]
    public async Task BytesThatAreNotATileAreReportedAsAnIoFailure()
    {
        var source = new FakeVectorTileSource { Bytes = [] };

        var exception = await Assert.ThrowsAsync<IOException>(
            () => new VectorRoadTileSource(source).GetTileAsync(14370, 6489, CancellationToken.None)).ConfigureAwait(true);

        Assert.Contains("z14/14370/6489", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFetchFailureIsReportedAsAnHttpRequestFailureWithTheCause()
    {
        var cause = new TileFetchException("offline", new Uri("https://tiles.example.com/14/1/2.pbf"), HttpStatusCode.ServiceUnavailable, innerException: null);
        var source = new FakeVectorTileSource { Failure = cause };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => new VectorRoadTileSource(source).GetTileAsync(1, 2, CancellationToken.None)).ConfigureAwait(true);

        Assert.Same(cause, exception.InnerException);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task CancellationIsNotConverted()
    {
        var source = new FakeVectorTileSource { Failure = new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new VectorRoadTileSource(source).GetTileAsync(1, 2, CancellationToken.None)).ConfigureAwait(true);
    }

    // レイヤー 1 つだけの、最小のベクタータイル（field 3 = layer。layer の field 1 = name、field 15 = version）
    private static byte[] RoadLayerTile(string layerName)
    {
        var name = System.Text.Encoding.UTF8.GetBytes(layerName);
        var layer = new List<byte> { 0x0A, (byte)name.Length };
        layer.AddRange(name);
        layer.AddRange([0x78, 0x02]);

        var tile = new List<byte> { 0x1A, (byte)layer.Count };
        tile.AddRange(layer);
        return [.. tile];
    }

    private sealed class FakeVectorTileSource : IVectorTileSource
    {
        public byte[]? Bytes { get; init; }

        public Exception? Failure { get; init; }

        public List<(int Z, int X, int Y)> Requests { get; } = new();

        public string SourceId => "fake";

        public int MaxZoom => 14;

        public Task<byte[]?> GetTileAsync(int z, int x, int y, TileFetchSession? session, CancellationToken cancellationToken = default)
        {
            Requests.Add((z, x, y));
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(Bytes);
        }
    }
}
