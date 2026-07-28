namespace SouthBaySoccer.Controls;

public partial class ToggleRow : ContentView
{
    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(nameof(Glyph), typeof(string), typeof(ToggleRow), string.Empty);
    public static readonly BindableProperty GlyphFontFamilyProperty = BindableProperty.Create(nameof(GlyphFontFamily), typeof(string), typeof(ToggleRow), string.Empty);
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(ToggleRow), string.Empty);
    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(ToggleRow), string.Empty);
    public static readonly BindableProperty IsToggledProperty = BindableProperty.Create(nameof(IsToggled), typeof(bool), typeof(ToggleRow), false, BindingMode.TwoWay);
    public ToggleRow() => InitializeComponent();
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string GlyphFontFamily { get => (string)GetValue(GlyphFontFamilyProperty); set => SetValue(GlyphFontFamilyProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public bool IsToggled { get => (bool)GetValue(IsToggledProperty); set => SetValue(IsToggledProperty, value); }
}
