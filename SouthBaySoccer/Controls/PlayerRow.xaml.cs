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
            propertyChanged: static (bindable, _, value) =>
                SemanticProperties.SetDescription((PlayerRow)bindable, (string)value));
    public static readonly BindableProperty DetailProperty =
        BindableProperty.Create(nameof(Detail), typeof(string), typeof(PlayerRow), null);
    public static readonly BindableProperty TrailingTextProperty =
        BindableProperty.Create(nameof(TrailingText), typeof(string), typeof(PlayerRow), null);
    public static readonly BindableProperty TrailingContentProperty =
        BindableProperty.Create(nameof(TrailingContent), typeof(View), typeof(PlayerRow));
    public static readonly BindableProperty TapCommandProperty =
        BindableProperty.Create(nameof(TapCommand), typeof(ICommand), typeof(PlayerRow));
    public static readonly BindableProperty TapCommandParameterProperty =
        BindableProperty.Create(nameof(TapCommandParameter), typeof(object), typeof(PlayerRow));

    public PlayerRow() => InitializeComponent();

    public string Initials { get => (string)GetValue(InitialsProperty); set => SetValue(InitialsProperty, value); }
    public ImageSource? ImageSource { get => (ImageSource?)GetValue(ImageSourceProperty); set => SetValue(ImageSourceProperty, value); }
    public string Name { get => (string)GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public string? Detail { get => (string?)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public string? TrailingText { get => (string?)GetValue(TrailingTextProperty); set => SetValue(TrailingTextProperty, value); }
    public View? TrailingContent { get => (View?)GetValue(TrailingContentProperty); set => SetValue(TrailingContentProperty, value); }
    public ICommand? TapCommand { get => (ICommand?)GetValue(TapCommandProperty); set => SetValue(TapCommandProperty, value); }
    public object? TapCommandParameter { get => GetValue(TapCommandParameterProperty); set => SetValue(TapCommandParameterProperty, value); }
}
