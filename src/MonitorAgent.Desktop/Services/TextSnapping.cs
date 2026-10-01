using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;

namespace MonitorAgent.Desktop.Services;

/// <summary>
/// Gives text blocks the pixel size the Windows app gives them. Avalonia rounds a text block's size up to a
/// whole unit and then up to a whole pixel, where WPF rounds to the nearest pixel, so at 125% every 11, 14 and
/// 16 pt line came out a pixel taller, labels a pixel or two wider, and the screens drifted apart.
/// </summary>
internal static class TextSnapping
{
    private static readonly ConditionalWeakTable<TextBlock, object> SnappedHeights = new();
    private static readonly ConditionalWeakTable<TextBlock, object> SnappedWidths = new();

    public static void Install()
    {
        Control.LoadedEvent.AddClassHandler<TextBlock>((block, _) => Snap(block));        foreach (var property in new AvaloniaProperty[]
                 {
                     TextBlock.TextProperty, TextBlock.FontSizeProperty, TextBlock.FontFamilyProperty,
                     TextBlock.FontWeightProperty, TextBlock.FontStyleProperty, TextBlock.TextWrappingProperty,
                     TextBlock.TextTrimmingProperty, TextBlock.PaddingProperty, Layoutable.HorizontalAlignmentProperty
                 })
        {
            property.Changed.AddClassHandler<TextBlock>((block, _) => Snap(block));
        }

        // Wrapped text can go from one line to several when its width changes.
        Visual.BoundsProperty.Changed.AddClassHandler<TextBlock>((block, _) =>
        {
            if (block.TextWrapping != TextWrapping.NoWrap)
            {
                Snap(block);
            }
        });
    }

    private static void Snap(TextBlock block)
    {
        if (!block.IsLoaded)
        {
            return;
        }

        Apply(block, TextBlock.LineHeightProperty, SnappedHeights, SnappedHeight);
        Apply(block, Layoutable.MaxWidthProperty, SnappedWidths, SnappedWidth);
    }

    private static void Apply(TextBlock block, StyledProperty<double> property, ConditionalWeakTable<TextBlock, object> snapped,
        Func<TextBlock, double?> compute)
    {
        var ours = snapped.TryGetValue(block, out _);
        if (!ours && block.IsSet(property))
        {
            return;
        }

        if (compute(block) is { } value)
        {
            block.SetValue(property, value);
            snapped.AddOrUpdate(block, value);
        }
        else if (ours)
        {
            block.ClearValue(property);
            snapped.Remove(block);
        }
    }

    private static double? SnappedHeight(TextBlock block)
    {
        // Text on several lines keeps its own height: the rounding then applies once to all the lines together.
        if (!block.UseLayoutRounding || block.Inlines is { Count: > 0 }
            || (block.TextWrapping != TextWrapping.NoWrap && block.TextLayout.TextLines.Count > 1))
        {
            return null;
        }

        var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
        if (!FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphs))
        {
            return null;
        }

        // WPF takes the line height from the requested font even when a symbol comes from a fallback font;
        // Avalonia takes the tallest font on the line.
        var metrics = glyphs.Metrics;
        var natural = (metrics.Descent - metrics.Ascent + metrics.LineGap) * block.FontSize / metrics.DesignEmHeight;
        var scale = LayoutHelper.GetLayoutScale(block);
        var target = Math.Round(natural * scale);
        var units = Math.Floor(target / scale + 1e-6);
        if (units <= 0 || Math.Ceiling(Math.Round(units * scale, 6)) != target)
        {
            return null;
        }

        using var layout = new TextLayout(string.IsNullOrEmpty(block.Text) ? " " : block.Text, typeface, block.FontSize, null);
        return units < Math.Ceiling(layout.Height - 1e-6) ? units : null;
    }

    private static double? SnappedWidth(TextBlock block)
    {
        // A narrower limit would wrap or trim the last letters, and a stretched block narrower than its slot
        // would be centred in it, so only one-line blocks that get exactly the width they ask for are snapped.
        if (!block.UseLayoutRounding || block.TextWrapping != TextWrapping.NoWrap || block.TextTrimming != TextTrimming.None
            || block.Padding != default || !GetsDesiredWidth(block))
        {
            return null;
        }

        var natural = block.TextLayout.WidthIncludingTrailingWhitespace;
        if (natural <= 0)
        {
            return null;
        }

        var scale = LayoutHelper.GetLayoutScale(block);
        var target = Math.Round(natural * scale) / scale;
        return target < Math.Ceiling(Math.Round(Math.Ceiling(natural - 1e-6) * scale, 6)) / scale - 1e-6 ? target : null;
    }

    private static bool GetsDesiredWidth(TextBlock block)
    {
        if (block.HorizontalAlignment != HorizontalAlignment.Stretch)
        {
            return true;
        }

        return block.GetVisualParent() switch
        {
            StackPanel panel => panel.Orientation == Orientation.Horizontal,
            WrapPanel panel => panel.Orientation == Orientation.Horizontal,
            DockPanel panel => DockPanel.GetDock(block) is Dock.Left or Dock.Right && !IsLastFilling(panel, block),
            ContentPresenter presenter => presenter.HorizontalContentAlignment != HorizontalAlignment.Stretch,
            _ => false
        };
    }

    private static bool IsLastFilling(DockPanel panel, Control block) =>
        panel.LastChildFill && panel.Children.Count > 0 && ReferenceEquals(panel.Children[^1], block);
}
