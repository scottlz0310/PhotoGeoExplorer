using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace PhotoGeoExplorer.Panes.Map;

/// <summary>
/// 子要素を横 1 行に並べ、幅が足りないときは優先度の順に非表示・末尾省略にするパネル。
/// 幅の割り当ては <see cref="PriorityCollapseLayout"/> が決める
/// </summary>
/// <remarks>
/// 添付プロパティは XAML ではなくコードから設定する。XAML コンパイラは internal 型の添付プロパティに
/// アクセサーを生成しないため、XAML で指定すると実行時に XamlParseException になる
/// </remarks>
internal sealed partial class PriorityCollapsePanel : Panel
{
    private const double MinTrimmedWidth = 32;

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(double),
        typeof(PriorityCollapsePanel),
        new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty PriorityProperty = DependencyProperty.RegisterAttached(
        "Priority",
        typeof(int),
        typeof(PriorityCollapsePanel),
        new PropertyMetadata(0));

    public static readonly DependencyProperty HideOnOverflowProperty = DependencyProperty.RegisterAttached(
        "HideOnOverflow",
        typeof(bool),
        typeof(PriorityCollapsePanel),
        new PropertyMetadata(false));

    public static readonly DependencyProperty TrimOnOverflowProperty = DependencyProperty.RegisterAttached(
        "TrimOnOverflow",
        typeof(bool),
        typeof(PriorityCollapsePanel),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsSeparatorProperty = DependencyProperty.RegisterAttached(
        "IsSeparator",
        typeof(bool),
        typeof(PriorityCollapsePanel),
        new PropertyMetadata(false));

    private double?[] _widths = Array.Empty<double?>();
    private OverflowState?[] _overflowStates = Array.Empty<OverflowState?>();

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static int GetPriority(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (int)element.GetValue(PriorityProperty);
    }

    public static void SetPriority(DependencyObject element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PriorityProperty, value);
    }

    public static bool GetHideOnOverflow(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(HideOnOverflowProperty);
    }

    public static void SetHideOnOverflow(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HideOnOverflowProperty, value);
    }

    public static bool GetTrimOnOverflow(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(TrimOnOverflowProperty);
    }

    public static void SetTrimOnOverflow(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TrimOnOverflowProperty, value);
    }

    public static bool GetIsSeparator(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsSeparatorProperty);
    }

    public static void SetIsSeparator(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsSeparatorProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Children.Count;
        var items = new PriorityCollapseItem[count];
        var unconstrained = new Size(double.PositiveInfinity, availableSize.Height);
        for (var i = 0; i < count; i++)
        {
            var child = Children[i];
            child.Measure(unconstrained);
            items[i] = new PriorityCollapseItem(
                child.DesiredSize.Width,
                GetPriority(child),
                GetMode(child),
                GetIsSeparator(child),
                IsPresent: child.Visibility == Visibility.Visible);
        }

        _widths = PriorityCollapseLayout.Compute(items, availableSize.Width, Spacing, MinTrimmedWidth);

        var totalWidth = 0.0;
        var height = 0.0;
        var shownCount = 0;
        for (var i = 0; i < count; i++)
        {
            if (_widths[i] is not { } width)
            {
                continue;
            }

            var child = Children[i];
            if (width < items[i].DesiredWidth)
            {
                child.Measure(new Size(width, availableSize.Height));
            }

            totalWidth += width;
            height = Math.Max(height, child.DesiredSize.Height);
            shownCount++;
        }

        totalWidth += Spacing * Math.Max(0, shownCount - 1);
        return new Size(Math.Min(totalWidth, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = Children.Count;
        if (_overflowStates.Length != count)
        {
            _overflowStates = new OverflowState?[count];
        }

        var x = 0.0;
        for (var i = 0; i < count; i++)
        {
            var child = Children[i];
            var width = i < _widths.Length ? _widths[i] : null;
            SetOverflowHidden(i, child, width is null && child.Visibility == Visibility.Visible);
            if (width is not { } shownWidth)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            child.Arrange(new Rect(x, 0, shownWidth, finalSize.Height));
            x += shownWidth + Spacing;
        }

        return finalSize;
    }

    private static PriorityCollapseMode GetMode(UIElement child)
    {
        if (GetHideOnOverflow(child))
        {
            return PriorityCollapseMode.Hide;
        }

        return GetTrimOnOverflow(child) ? PriorityCollapseMode.Trim : PriorityCollapseMode.Keep;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PriorityCollapsePanel)d).InvalidateMeasure();

    // 幅 0 に配置しただけでは、フォーカス移動と UI オートメーションからは見えたままになる。
    // 再表示のときに戻せるよう、隠す前の値を控えておく
    private void SetOverflowHidden(int index, UIElement child, bool hidden)
    {
        if (hidden == _overflowStates[index].HasValue)
        {
            return;
        }

        if (hidden)
        {
            _overflowStates[index] = new OverflowState(
                child.IsHitTestVisible,
                child.IsTabStop,
                AutomationProperties.GetAccessibilityView(child));
            child.IsHitTestVisible = false;
            child.IsTabStop = false;
            AutomationProperties.SetAccessibilityView(child, AccessibilityView.Raw);
            return;
        }

        var state = _overflowStates[index]!.Value;
        _overflowStates[index] = null;
        child.IsHitTestVisible = state.IsHitTestVisible;
        child.IsTabStop = state.IsTabStop;
        AutomationProperties.SetAccessibilityView(child, state.AccessibilityView);
    }

    private readonly record struct OverflowState(bool IsHitTestVisible, bool IsTabStop, AccessibilityView AccessibilityView);
}
