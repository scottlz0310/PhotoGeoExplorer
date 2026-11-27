using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhotoGeoExplorer.Panes.Map;

namespace PhotoGeoExplorer.Tests;

/// <summary>
/// 呼び出しを記録する <see cref="IMapImageExporter"/>。条件の検査は本物の上下限を使う。
/// </summary>
internal sealed class FakeMapImageExporter : IMapImageExporter
{
    /// <summary>タイル枚数の見積もり。上限を超える値にすると、作れない条件になる</summary>
    public Func<MapImageExportRequest, int> CountTiles { get; set; } = static _ => 12;

    /// <summary>描画の中身。既定は、何回目の描画かを 1 バイトで返す</summary>
    public Func<MapImageExportRequest, CancellationToken, Task<Stream>>? Render { get; set; }

    public List<MapImageExportRequest> Rendered { get; } = [];

    public int DisposeCount { get; private set; }

    /// <summary>検査結果を差し替える。<see langword="null"/> を返すと、本物の上下限とタイル枚数で判定する</summary>
    public Func<MapImageExportRequest, MapImageExportPlan?> OverridePlan { get; set; } = static _ => null;

    public MapImageExportPlan Plan(MapImageExportRequest request)
    {
        if (OverridePlan(request) is { } overridden)
        {
            return overridden;
        }

        var problem = MapImageExportLimits.Validate(request);
        if (problem != MapImageExportProblem.None)
        {
            return new MapImageExportPlan(problem, 0);
        }

        var tileCount = CountTiles(request);
        return new MapImageExportPlan(
            tileCount > MapImageExportLimits.MaxTileCount ? MapImageExportProblem.TooManyTiles : MapImageExportProblem.None,
            tileCount);
    }

    public Task<Stream> RenderPngAsync(MapImageExportRequest request, CancellationToken cancellationToken)
    {
        Rendered.Add(request);
        return Render is { } render
            ? render(request, cancellationToken)
            : Task.FromResult<Stream>(new MemoryStream([(byte)Rendered.Count]));
    }

    public void Dispose() => DisposeCount++;
}
