namespace PhotoGeoExplorer.Services.PanZoom;

/// <summary>
/// ジェスチャー認識の結果を受ける側。画像プレビューと地図は、同じ入力アダプタからこの契約へ入力を受ける。
/// </summary>
internal interface IPanZoomTarget
{
    /// <summary>
    /// ジェスチャー 1 回分の変化を適用する。<paramref name="pivot"/> を基準に倍率を掛けたあと、移動量を加える。
    /// </summary>
    /// <param name="pivot">拡大の基準（入力を受ける要素の左上が原点）</param>
    /// <param name="scaleFactor">倍率の変化</param>
    /// <param name="translationX">X の移動量</param>
    /// <param name="translationY">Y の移動量</param>
    void Manipulate(PanZoomPoint pivot, double scaleFactor, double translationX, double translationY);

    /// <summary>ジェスチャーが終わった（指やボタンが離れた。OS の慣性を使う場合は、慣性の終了）。</summary>
    void CompleteManipulation();

    /// <summary>ホイールの回転量（1 ノッチは 120）を、<paramref name="anchor"/> を基準にしたズームとして適用する。</summary>
    /// <param name="wheelDelta">回転量</param>
    /// <param name="anchor">拡大の基準（入力を受ける要素の左上が原点）</param>
    /// <param name="isHorizontal">横方向のホイール（チルトホイールなど）か。受けるかどうかは受け手が決める</param>
    /// <returns>この入力を使ったとき <see langword="true"/>。<see langword="false"/> なら、入力は親の要素へ伝わる</returns>
    bool ZoomByWheel(int wheelDelta, PanZoomPoint anchor, bool isHorizontal);
}
