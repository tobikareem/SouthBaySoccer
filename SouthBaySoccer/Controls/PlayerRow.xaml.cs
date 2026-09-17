using System.Windows.Input;

namespace SouthBaySoccer.Controls;

public partial class PlayerRow
{
    public static readonly BindableProperty InitialsProperty =
        BindableProperty.Create(nameof(Initials), typeof(string), typeof(PlayerRow), string.Empty);
    public static readonly BindableProperty ImageSourceProperty =
        BindableProperty.Create(nameof(ImageSource), typeof(ImageSource), typeof(PlayerRow));
    public static readonly BindableProperty NameProperty =
        BindableProperty.Create(nameof(Name), typeof(string), typeof(PlayerRow), string.Empty,
            propertyChanged: static (bindable, _, _) => ((PlayerRow)bindable).UpdateSemanticDescription());
    public static readonly BindableProperty SemanticDescriptionProperty =
        BindableProperty.Create(nameof(SemanticDescription), typeof(string), typeof(PlayerRow), null,
            propertyChanged: static (bindable, _, _) => ((PlayerRow)bindable).UpdateSemanticDescription());
    public static readonly BindableProperty DetailProperty =
        BindableProperty.Create(nameof(Detail), typeof(string), typeof(PlayerRow), null);
    public static readonly BindableProperty TrailingTextProperty =
        BindableProperty.Create(nameof(TrailingText), typeof(string), typeof(PlayerRow), null);
    public static readonly BindableProperty LeadingContentProperty =
        BindableProperty.Create(nameof(LeadingContent), typeof(View), typeof(PlayerRow),
            propertyChanged: static (bindable, _, value) =>
                ((PlayerRow)bindable).LeadingSlot.IsVisible = value is not null);
    public static readonly BindableProperty TrailingContentProperty =
        BindableProperty.Create(nameof(TrailingContent), typeof(View), typeof(PlayerRow));
    public static readonly BindableProperty TapCommandProperty =
        BindableProperty.Create(nameof(TapCommand), typeof(ICommand), typeof(PlayerRow));
    public static readonly BindableProperty TapCommandParameterProperty =
        BindableProperty.Create(nameof(TapCommandParameter), typeof(object), typeof(PlayerRow));
    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(nameof(Glyph), typeof(string), typeof(PlayerRow), null,
            propertyChanged: static (bindable, _, value) =>
            {
                // Additive: a set glyph turns the row into a menu row (icon tile); unset keeps
                // the person avatar exactly as before.
                var row = (PlayerRow)bindable;
                var hasGlyph = value is string glyph && !string.IsNullOrWhiteSpace(glyph);
                row.GlyphSlot.IsVisible = hasGlyph;
                row.AvatarSlot.IsVisible = !hasGlyph;
            });
    public static readonly BindableProperty GlyphFontFamilyProperty =
        BindableProperty.Create(nameof(GlyphFontFamily), typeof(string), typeof(PlayerRow), "FontAwesomeSolid");

    public PlayerRow() => InitializeComponent();

    public string Initials { get => (string)GetValue(InitialsProperty); set => SetValue(InitialsProperty, value); }
    public ImageSource? ImageSource { get => (ImageSource?)GetValue(ImageSourceProperty); set => SetValue(ImageSourceProperty, value); }
    public string Name { get => (string)GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public string? SemanticDescription { get => (string?)GetValue(SemanticDescriptionProperty); set => SetValue(SemanticDescriptionProperty, value); }
    public string? Detail { get => (string?)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public string? TrailingText { get => (string?)GetValue(TrailingTextProperty); set => SetValue(TrailingTextProperty, value); }
    public View? LeadingContent { get => (View?)GetValue(LeadingContentProperty); set => SetValue(LeadingContentProperty, value); }
    public View? TrailingContent { get => (View?)GetValue(TrailingContentProperty); set => SetValue(TrailingContentProperty, value); }
    public ICommand? TapCommand { get => (ICommand?)GetValue(TapCommandProperty); set => SetValue(TapCommandProperty, value); }
    public object? TapCommandParameter { get => GetValue(TapCommandParameterProperty); set => SetValue(TapCommandParameterProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string GlyphFontFamily { get => (string)GetValue(GlyphFontFamilyProperty); set => SetValue(GlyphFontFamilyProperty, value); }

    private void UpdateSemanticDescription() =>
        SemanticProperties.SetDescription(
            this,
            string.IsNullOrWhiteSpace(SemanticDescription) ? Name : SemanticDescription);
}
