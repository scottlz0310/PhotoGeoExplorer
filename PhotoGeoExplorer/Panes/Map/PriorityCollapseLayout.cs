using System;
using System.Collections.Generic;

namespace PhotoGeoExplorer.Panes.Map;

internal enum PriorityCollapseMode
{
    /// <summary>幅が足りなくても、そのまま表示する</summary>
    Keep = 0,

    /// <summary>幅が足りないとき、非表示にする</summary>
    Hide = 1,

    /// <summary>幅が足りないとき、幅を詰める（文字列は末尾省略になる）</summary>
    Trim = 2
}

/// <param name="DesiredWidth">幅の制約なしで測った幅</param>
/// <param name="Priority">大きいほど先に省略する</param>
/// <param name="IsSeparator">前後の両方に表示中の項目があるときだけ表示する区切り</param>
/// <param name="IsPresent">レイアウトの対象か（Collapsed の要素は false）</param>
internal readonly record struct PriorityCollapseItem(
    double DesiredWidth,
    int Priority = 0,
    PriorityCollapseMode Mode = PriorityCollapseMode.Keep,
    bool IsSeparator = false,
    bool IsPresent = true);

/// <summary>
/// <see cref="PriorityCollapsePanel"/> の幅の割り当て。UI 要素に依存しない
/// </summary>
internal static class PriorityCollapseLayout
{
    /// <returns>各要素に割り当てる幅。表示しない要素は null</returns>
    public static double?[] Compute(
        IReadOnlyList<PriorityCollapseItem> items,
        double availableWidth,
        double spacing,
        double minTrimmedWidth)
    {
        ArgumentNullException.ThrowIfNull(items);

        var widths = new double?[items.Count];
        var pending = new List<int>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!item.IsPresent || item.IsSeparator)
            {
                continue;
            }

            widths[i] = item.DesiredWidth;
            if (item.Mode != PriorityCollapseMode.Keep)
            {
                pending.Add(i);
            }
        }

        // 優先度の数値が大きいものから省略する。同じ優先度なら後ろの要素から
        pending.Sort((left, right) =>
        {
            var byPriority = items[right].Priority.CompareTo(items[left].Priority);
            return byPriority != 0 ? byPriority : right.CompareTo(left);
        });

        var next = 0;
        while (true)
        {
            var total = AssignSeparators(items, widths, spacing);
            var excess = total - availableWidth;
            if (excess <= 0 || next == pending.Count)
            {
                return widths;
            }

            var index = pending[next++];
            if (items[index].Mode == PriorityCollapseMode.Hide)
            {
                widths[index] = null;
                continue;
            }

            var desired = items[index].DesiredWidth;
            widths[index] = Math.Max(Math.Min(minTrimmedWidth, desired), desired - excess);
        }
    }

    // 区切りの表示可否を決め直し、表示する要素と間隔の合計幅を返す
    private static double AssignSeparators(IReadOnlyList<PriorityCollapseItem> items, double?[] widths, double spacing)
    {
        var total = 0.0;
        var shownCount = 0;
        var hasItemBefore = false;
        var candidateSeparator = -1;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.IsSeparator)
            {
                widths[i] = null;
                if (item.IsPresent && hasItemBefore && candidateSeparator < 0)
                {
                    candidateSeparator = i;
                }

                continue;
            }

            if (widths[i] is not { } width)
            {
                continue;
            }

            if (candidateSeparator >= 0)
            {
                widths[candidateSeparator] = items[candidateSeparator].DesiredWidth;
                total += items[candidateSeparator].DesiredWidth;
                shownCount++;
                candidateSeparator = -1;
            }

            total += width;
            shownCount++;
            hasItemBefore = true;
        }

        return total + (spacing * Math.Max(0, shownCount - 1));
    }
}
