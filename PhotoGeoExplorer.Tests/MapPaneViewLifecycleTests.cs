using System;
using System.Collections.Generic;
using System.Linq;
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

        var logs = new List<string>();
        var lifecycle = new MapPaneViewLifecycle(logs.Add);
        var decisions = new List<char>();

        foreach (var step in steps)
        {
            switch (step)
            {
                case 'L':
                    lifecycle.NotifyLoaded("state");
                    break;
                case 'U':
                    decisions.Add(lifecycle.TryNotifyUnloaded(isLoadedNow: false, viewState: "state") ? 'T' : '-');
                    break;
                case 'u':
                    decisions.Add(lifecycle.TryNotifyUnloaded(isLoadedNow: true, viewState: "state") ? 'T' : '-');
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(steps), step, "Unknown step.");
            }
        }

        Assert.Equal(expectedDecisions, new string([.. decisions]));
        Assert.Equal(expectedLoaded, lifecycle.IsViewLoaded);

        // 通知ごとに 1 件ずつ記録され、無視した Unloaded は無視したと分かること
        Assert.Equal(steps.Length, logs.Count);
        Assert.Equal(expectedDecisions.Count(d => d == '-'), logs.Count(l => l.Contains("ignored", StringComparison.Ordinal)));
        Assert.All(logs, l => Assert.Contains("state", l, StringComparison.Ordinal));
    }

    [Fact]
    public void ConstructorThrowsWhenLogIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new MapPaneViewLifecycle(null!));
    }
}
