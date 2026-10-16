using System;
using System.Collections.Generic;
using PhotoGeoExplorer.Panes.Map;
using Xunit;

namespace PhotoGeoExplorer.Tests;

public sealed class MapPaneViewLifecycleTests
{
    // 手順: "L" = Loaded、"U" = Unloaded（その時点でツリー外）、"u" = Unloaded（その時点で既にツリーへ戻っている）
    // 期待する解除判断: "T" = 解除する、"-" = 古い通知として無視する
    [Theory]
    [InlineData("", "", false)]
    [InlineData("L", "", true)]
    [InlineData("LU", "T", false)]
    [InlineData("ULL", "T", true)]
    [InlineData("UL", "T", true)]
    [InlineData("Lu", "-", true)]
    [InlineData("LuU", "-T", false)]
    [InlineData("LULu", "T-", true)]
    [InlineData("LuLu", "--", true)]
    public void TracksLoadedStateAndDecidesTeardown(string steps, string expectedDecisions, bool expectedLoaded)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var lifecycle = new MapPaneViewLifecycle();
        var decisions = new List<char>();

        foreach (var step in steps)
        {
            switch (step)
            {
                case 'L':
                    lifecycle.NotifyLoaded();
                    break;
                case 'U':
                    decisions.Add(lifecycle.TryNotifyUnloaded(isLoadedNow: false) ? 'T' : '-');
                    break;
                case 'u':
                    decisions.Add(lifecycle.TryNotifyUnloaded(isLoadedNow: true) ? 'T' : '-');
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(steps), step, "Unknown step.");
            }
        }

        Assert.Equal(expectedDecisions, new string([.. decisions]));
        Assert.Equal(expectedLoaded, lifecycle.IsViewLoaded);
    }
}
