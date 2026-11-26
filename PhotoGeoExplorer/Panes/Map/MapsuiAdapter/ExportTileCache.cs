using System;
using System.IO;
using BruTile;
using BruTile.Cache;
using PhotoGeoExplorer.Services;

namespace PhotoGeoExplorer.Panes.Map.MapsuiAdapter;

/// <summary>
/// 保存用の配信元が使うタイルキャッシュ。置き場は表示中の背景と共有するため、検証していない応答は書かず、
/// 消すのは壊れていると確かめた内容が入っている場合だけにする。
/// </summary>
/// <remarks>
/// 表示側が同じタイルを読み書きしている間は、ファイルを開けないことがある。キャッシュの失敗では画像の作成を止めず、記録する。
/// </remarks>
internal sealed class ExportTileCache(IPersistentCache<byte[]> shared) : IPersistentCache<byte[]>
{
    public byte[]? Find(TileIndex index)
    {
        try
        {
            return shared.Find(index);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("The map tile cache could not be read.", ex);
            return null;
        }
    }

    /// <summary>
    /// 配信元が取得の直後に呼ぶ。内容が検証前のため書かない（確かめた内容は <see cref="Store"/> で書く）
    /// </summary>
    public void Add(TileIndex index, byte[] tile)
    {
    }

    public void Remove(TileIndex index) => shared.Remove(index);

    /// <summary>
    /// 検証した画像を書く。すでにあるタイルは上書きされない
    /// </summary>
    internal void Store(TileIndex index, byte[] verified)
    {
        try
        {
            shared.Add(index, verified);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("The map tile cache could not be written.", ex);
        }
    }

    /// <summary>
    /// 壊れていると確かめた内容がキャッシュにあれば消す。別の内容（表示側が書いた画像など）は残す
    /// </summary>
    internal void Discard(TileIndex index, byte[] broken)
    {
        try
        {
            if (shared.Find(index) is { } cached && cached.AsSpan().SequenceEqual(broken))
            {
                shared.Remove(index);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("The broken map tile could not be removed from the cache.", ex);
        }
    }
}
