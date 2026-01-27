using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace PhotoGeoExplorer.Services;

/// <summary>
/// Windows Explorer 準拠の自然順ソートを行う比較子。
/// 数値を含むファイル名を「1, 2, 3, 11」のように数値として比較します。
/// </summary>
internal sealed class NaturalSortComparer : IComparer<string?>
{
    /// <summary>
    /// 大文字小文字を区別しないシングルトンインスタンス。
    /// </summary>
    public static NaturalSortComparer Instance { get; } = new();

    private NaturalSortComparer()
    {
    }

    /// <summary>
    /// 2 つの文字列を自然順で比較します。
    /// </summary>
    /// <param name="x">比較する最初の文字列。</param>
    /// <param name="y">比較する 2 番目の文字列。</param>
    /// <returns>
    /// x が y より小さい場合は負の値、等しい場合は 0、大きい場合は正の値。
    /// </returns>
    public int Compare(string? x, string? y)
    {
        // null ハンドリング
        if (x is null && y is null)
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        // Windows API を使用して自然順比較
        return StrCmpLogicalW(x, y);
    }

    /// <summary>
    /// Windows Shell API: 文字列を自然順（数値を数値として）で比較します。
    /// </summary>
    /// <param name="psz1">比較する最初の文字列。</param>
    /// <param name="psz2">比較する 2 番目の文字列。</param>
    /// <returns>
    /// psz1 が psz2 より小さい場合は負の値、等しい場合は 0、大きい場合は正の値。
    /// </returns>
    /// <remarks>
    /// Windows Explorer と同じソート順を実現するため、P/Invoke を使用しています。
    /// マネージド コードでは Windows Explorer の動作を完全に再現できません。
    /// </remarks>
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Windows Explorer 準拠の自然順ソートを実現するため、Windows API の使用が必須です。マネージド コードでは同等の動作を実現できません。")]
    [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = "このプロジェクトでは従来の DllImport を使用する方針です。LibraryImport への移行は将来的な検討事項です。")]
    private static extern int StrCmpLogicalW(string psz1, string psz2);
}
