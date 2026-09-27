using System.Windows.Input;

namespace SouthBaySoccer.Controls;

public partial class SectionHeader
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(SectionHeader), string.Empty);
    public static readonly BindableProperty ActionTextProperty =
        BindableProperty.Create(nameof(ActionText), typeof(string), typeof(SectionHeader), string.Empty);
    public static readonly BindableProperty ActionCommandProperty =
        BindableProperty.Create(nameof(ActionCommand), typeof(ICommand), typeof(SectionHeader));

    /// <summary>
    /// Optional Font Awesome glyph shown before <see cref="ActionText"/>. Rendered through the
    /// button's own ImageSource rather than a prepended character so the glyph keeps the icon font
    /// while the label keeps the brand text font.
    /// </summary>
    public static readonly BindableProperty ActionGlyphProperty =
        BindableProperty.Create(nameof(ActionGlyph), typeof(string), typeof(SectionHeader), string.Empty,
            propertyChanged: static (bindable, _, _) => ((SectionHeader)bindable).RefreshVisibility());

    /// <summary>
    /// Optional second action, laid out inline to the right of the first. Hidden unless its text is
    /// set, so every existing single-action header keeps rendering unchanged.
    /// </summary>
    public static readonly BindableProperty SecondaryActionTextProperty =
        BindableProperty.Create(nameof(SecondaryActionText), typeof(string), typeof(SectionHeader), string.Empty,
            propertyChanged: static (bindable, _, _) => ((SectionHeader)bindable).RefreshVisibility());
    public static readonly BindableProperty SecondaryActionCommandProperty =
        BindableProperty.Create(nameof(SecondaryActionCommand), typeof(ICommand), typeof(SectionHeader));
    public static readonly BindableProperty SecondaryActionGlyphProperty =
        BindableProperty.Create(nameof(SecondaryActionGlyph), typeof(string), typeof(SectionHeader), string.Empty,
            propertyChanged: static (bindable, _, _) => ((SectionHeader)bindable).RefreshVisibility());

    public SectionHeader()
    {
        InitializeComponent();
        RefreshVisibility();
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string ActionText { get => (string)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public string ActionGlyph { get => (string)GetValue(ActionGlyphProperty); set => SetValue(ActionGlyphProperty, value); }
    public string SecondaryActionText { get => (string)GetValue(SecondaryActionTextProperty); set => SetValue(SecondaryActionTextProperty, value); }
    public ICommand? SecondaryActionCommand { get => (ICommand?)GetValue(SecondaryActionCommandProperty); set => SetValue(SecondaryActionCommandProperty, value); }
    public string SecondaryActionGlyph { get => (string)GetValue(SecondaryActionGlyphProperty); set => SetValue(SecondaryActionGlyphProperty, value); }

    private void RefreshVisibility()
    {
        // Assigned in code rather than bound in XAML: a FontImageSource with an empty Glyph still
        // reserves image space on the button, which would put a stray gap in front of the label on
        // every existing single-action header. A null ImageSource reserves nothing.
        ActionButton.ImageSource = BuildGlyph(ActionGlyph);
        SecondaryActionButton.ImageSource = BuildGlyph(SecondaryActionGlyph);
        SecondaryActionButton.IsVisible = !string.IsNullOrWhiteSpace(SecondaryActionText);
    }

    private static ImageSource? BuildGlyph(string glyph)
    {
        if (string.IsNullOrWhiteSpace(glyph))
        {
            return null;
        }

        var source = new FontImageSource { Glyph = glyph, FontFamily = "FontAwesomeSolid", Size = 13 };
        var resources = Application.Current!.Resources;
        source.SetAppTheme(
            FontImageSource.ColorProperty,
            (Color)resources["BrandGreenLight"],
            (Color)resources["BrandSpringDark"]);
        return source;
    }
}
