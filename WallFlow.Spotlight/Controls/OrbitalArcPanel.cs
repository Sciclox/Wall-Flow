using System;
using System.Windows;
using System.Windows.Controls;

namespace WallFlow.Spotlight.Controls;

/// <summary>
/// Custom WPF Panel that arranges its children along a smooth parabolic gravitational arc.
/// Center children are elevated higher, creating an organic spatial constellation without containers.
/// </summary>
public class OrbitalArcPanel : Panel
{
    public static readonly DependencyProperty ArcHeightProperty =
        DependencyProperty.Register(
            nameof(ArcHeight),
            typeof(double),
            typeof(OrbitalArcPanel),
            new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing),
            typeof(double),
            typeof(OrbitalArcPanel),
            new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double ArcHeight
    {
        get => (double)GetValue(ArcHeightProperty);
        set => SetValue(ArcHeightProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double totalWidth = 0;
        double maxHeight = 0;
        int count = InternalChildren.Count;

        for (int i = 0; i < count; i++)
        {
            UIElement child = InternalChildren[i];
            if (child == null || child.Visibility == Visibility.Collapsed) continue;

            child.Measure(availableSize);
            totalWidth += child.DesiredSize.Width;
            maxHeight = Math.Max(maxHeight, child.DesiredSize.Height);
        }

        int visibleCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (InternalChildren[i].Visibility != Visibility.Collapsed) visibleCount++;
        }

        if (visibleCount > 1)
        {
            totalWidth += (visibleCount - 1) * Spacing;
        }

        return new Size(totalWidth, maxHeight + ArcHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = InternalChildren.Count;
        if (count == 0) return finalSize;

        double totalWidth = 0;
        int visibleCount = 0;
        for (int i = 0; i < count; i++)
        {
            UIElement child = InternalChildren[i];
            if (child.Visibility == Visibility.Collapsed) continue;
            totalWidth += child.DesiredSize.Width;
            visibleCount++;
        }

        if (visibleCount > 1)
        {
            totalWidth += (visibleCount - 1) * Spacing;
        }

        double currentX = (finalSize.Width - totalWidth) / 2.0;
        double halfSpan = Math.Max(1.0, totalWidth / 2.0);

        for (int i = 0; i < count; i++)
        {
            UIElement child = InternalChildren[i];
            if (child.Visibility == Visibility.Collapsed) continue;

            double w = child.DesiredSize.Width;
            double h = child.DesiredSize.Height;

            // Parabolic curvature calculation
            double childCenterX = currentX + w / 2.0;
            double t = (childCenterX - (finalSize.Width / 2.0)) / halfSpan;
            t = Math.Max(-1.0, Math.Min(1.0, t));

            double lift = ArcHeight * Math.Max(0.0, 1.0 - (t * t));
            double y = Math.Max(0.0, finalSize.Height - h - lift);

            child.Arrange(new Rect(currentX, y, w, h));
            currentX += w + Spacing;
        }

        return finalSize;
    }
}
