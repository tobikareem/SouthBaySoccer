namespace SouthBaySoccer.Controls;

[ContentProperty(nameof(Body))]
public partial class BrandCard
{
    public static readonly BindableProperty BodyProperty =
        BindableProperty.Create(
            nameof(Body),
            typeof(View),
            typeof(BrandCard),
            propertyChanged: static (bindable, _, value) =>
                ((BrandCard)bindable).ApplyBody((View?)value));
    public static readonly BindableProperty IsTintedProperty =
        BindableProperty.Create(nameof(IsTinted), typeof(bool), typeof(BrandCard), false,
            propertyChanged: static (bindable, _, _) => ((BrandCard)bindable).ApplyAppearance());
    public static readonly BindableProperty IsHeroProperty =
        BindableProperty.Create(nameof(IsHero), typeof(bool), typeof(BrandCard), false,
            propertyChanged: static (bindable, _, _) => ((BrandCard)bindable).ApplyAppearance());

    public BrandCard()
    {
        InitializeComponent();
        Padding = (Thickness)Application.Current!.Resources["CardPadding"];
        ApplyAppearance();
    }

    public View? Body { get => (View?)GetValue(BodyProperty); set => SetValue(BodyProperty, value); }
    public bool IsTinted { get => (bool)GetValue(IsTintedProperty); set => SetValue(IsTintedProperty, value); }
    public bool IsHero { get => (bool)GetValue(IsHeroProperty); set => SetValue(IsHeroProperty, value); }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (Body is not null)
        {
            SetInheritedBindingContext(Body, BindingContext);
        }
    }

    private void ApplyBody(View? body)
    {
        BodyPresenter.Content = body;
        if (body is not null)
        {
            SetInheritedBindingContext(body, BindingContext);
        }
    }

    private void ApplyAppearance()
    {
        var styleKey = IsHero ? "HeroCardSurface" : IsTinted ? "TintSurface" : "CardSurface";
        CardBorder.Style = (Style)Application.Current!.Resources[styleKey];
    }
}
