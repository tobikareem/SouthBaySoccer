namespace SouthBaySoccer.Controls;

public partial class Badge
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(Badge), string.Empty,
            propertyChanged: static (bindable, _, value) =>
                SemanticProperties.SetDescription((Badge)bindable, (string)value));
    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(nameof(Glyph), typeof(string), typeof(Badge), null);
    public static readonly BindableProperty VariantProperty =
        BindableProperty.Create(nameof(Variant), typeof(BadgeVariant), typeof(Badge), BadgeVariant.Neutral,
            propertyChanged: static (bindable, _, _) => ((Badge)bindable).ApplyVariant());

    public Badge()
    {
        InitializeComponent();
        ApplyVariant();
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public BadgeVariant Variant { get => (BadgeVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    private void ApplyVariant()
    {
        var resources = Application.Current!.Resources;
        var (lightBackground, darkBackground, lightForeground, darkForeground) = Variant switch
        {
            BadgeVariant.Success => ("BrandMistLight", "BrandMistDark", "BrandGreenDepthLight", "BrandSpringDark"),
            BadgeVariant.Warning => ("WarningSurfaceLight", "WarningSurfaceDark", "WarningLight", "WarningDark"),
            BadgeVariant.Danger => ("DangerSurfaceLight", "DangerSurfaceDark", "DangerLight", "DangerDark"),
            _ => ("SurfaceAltLight", "SurfaceAltDark", "BrandSageLight", "BrandSageDark")
        };
        Pill.SetAppThemeColor(BackgroundColorProperty, (Color)resources[lightBackground],
            (Color)resources[darkBackground]);
        TextLabel.SetAppThemeColor(Label.TextColorProperty, (Color)resources[lightForeground],
            (Color)resources[darkForeground]);
        GlyphLabel.SetAppThemeColor(Label.TextColorProperty, (Color)resources[lightForeground],
            (Color)resources[darkForeground]);
    }
}
