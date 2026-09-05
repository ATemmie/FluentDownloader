using System.Windows;
using System.Windows.Controls;

namespace FluentDownloader.Shared.Services;

/// <summary>
/// WPF Frame（WPF-UI 的 NavigationViewContentPresenter 继承自它）以"无限高度"测量页面，
/// 页面内的 ScrollViewer 因此认为自己不需要滚动，内容直接被窗口裁掉。
/// 给页面根元素挂上 Enable="True" 后，会把元素的 MaxHeight
/// 设为宿主 Frame 的实际高度并随窗口尺寸变化更新，滚动即可恢复正常。
/// </summary>
public static class FrameHeightConstraint
{
    private static readonly DependencyProperty AppliedProperty =
        DependencyProperty.RegisterAttached("Applied", typeof(bool), typeof(FrameHeightConstraint),
            new PropertyMetadata(false));

    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached("Enable", typeof(bool), typeof(FrameHeightConstraint),
            new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnable(DependencyObject d, bool value) => d.SetValue(EnableProperty, value);

    public static bool GetEnable(DependencyObject d) => (bool)d.GetValue(EnableProperty);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        if ((bool)e.NewValue)
            element.Loaded += OnElementLoaded;
        else
            element.Loaded -= OnElementLoaded;
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if ((bool)element.GetValue(AppliedProperty)) return; // 页面反复 Loaded 时避免重复挂接

        if (FindFrame(element) is not Frame frame) return;

        element.SetValue(AppliedProperty, true);
        void Update() => element.MaxHeight = frame.ActualHeight;
        Update();
        frame.SizeChanged += (_, _) => Update();
    }

    private static Frame? FindFrame(FrameworkElement element)
    {
        var d = System.Windows.Media.VisualTreeHelper.GetParent(element);
        while (d != null)
        {
            if (d is Frame frame) return frame;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}
