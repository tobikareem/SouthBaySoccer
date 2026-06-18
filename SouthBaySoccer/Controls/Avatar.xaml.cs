namespace SouthBaySoccer.Controls;

public partial class Avatar
{
    public static readonly BindableProperty InitialsProperty =
        BindableProperty.Create(nameof(Initials), typeof(string), typeof(Avatar), string.Empty);
    public static readonly BindableProperty ImageSourceProperty =
        BindableProperty.Create(nameof(ImageSource), typeof(ImageSource), typeof(Avatar), null,
            propertyChanged: static (bindable, _, value) => ((Avatar)bindable).AvatarImage.IsVisible = value is not null);
    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(Avatar), 34d);
    public static readonly BindableProperty VariantProperty =
        BindableProperty.Create(nameof(Variant), typeof(AvatarVariant), typeof(Avatar), AvatarVariant.Mist,
            propertyChanged: static (bindable, _, _) => ((Avatar)bindable).ApplyVariant());
    public static readonly BindableProperty DescriptionProperty =
        BindableProperty.Create(nameof(Description), typeof(string), typeof(Avatar), "Player avatar",
            propertyChanged: static (bindable, _, value) =>
                SemanticProperties.SetDescription((Avatar)bindable, (string)value));

    public Avatar()
    {
        InitializeComponent();
        AvatarImage.IsVisible = ImageSource is not null;
        ApplyVariant();
    }

    public string Initials { get => (string)GetValue(InitialsProperty); set => SetValue(InitialsProperty, value); }
    public ImageSource? ImageSource { get => (ImageSource?)GetValue(ImageSourceProperty); set => SetValue(ImageSourceProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public AvatarVariant Variant { get => (AvatarVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    private void ApplyVariant()
    {
        var resources = Application.Current!.Resources;
        if (Variant == AvatarVariant.OnGreen)
        {
            AvatarBorder.BackgroundColor = (Color)resources["HeaderOverlay"];
            InitialsLabel.TextColor = Colors.White;
            return;
        }
        AvatarBorder.SetAppThemeColor(BackgroundColorProperty,
            (Color)resources["BrandMistLight"], (Color)resources["BrandMistDark"]);
        InitialsLabel.SetAppThemeColor(Label.TextColorProperty,
            (Color)resources["BrandGreenDepthLight"], (Color)resources["BrandSpringDark"]);
    }
}
