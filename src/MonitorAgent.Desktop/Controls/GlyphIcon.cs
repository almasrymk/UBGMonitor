using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons.Avalonia;
using MonitorAgent.Desktop.Converters;

namespace MonitorAgent.Desktop.Controls;

/// <summary>
/// A Segoe MDL2 Assets glyph, the icon font the Windows app uses. The font comes with Windows only, so other
/// systems show the matching Material icon instead.
/// </summary>
public sealed class GlyphIcon : Decorator
{
    public static readonly StyledProperty<string?> GlyphProperty =
        AvaloniaProperty.Register<GlyphIcon, string?>(nameof(Glyph));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<GlyphIcon, double>(nameof(Size), 12);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<GlyphIcon>();

    private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

    public string? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public GlyphIcon()
    {
        VerticalAlignment = VerticalAlignment.Center;
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GlyphProperty || change.Property == SizeProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        if (OperatingSystem.IsWindows())
        {
            if (Child is not TextBlock text)
            {
                Child = text = new TextBlock { FontFamily = IconFont };
            }

            text.Text = Glyph;
            text.FontSize = Size;
            return;
        }

        if (Child is not MaterialIcon icon)
        {
            Child = icon = new MaterialIcon();
        }

        icon.Kind = GlyphToIconConverter.Icon(Glyph);
        icon.Width = icon.Height = Size * 1.15;
    }
}
