namespace SouthBaySoccer.Controls;

public partial class CapacityBar
{
    public static readonly BindableProperty CurrentProperty =
        BindableProperty.Create(nameof(Current), typeof(int), typeof(CapacityBar), 0,
            propertyChanged: OnValueChanged);
    public static readonly BindableProperty MaxProperty =
        BindableProperty.Create(nameof(Max), typeof(int), typeof(CapacityBar), 1,
            propertyChanged: OnValueChanged);
    public static readonly BindableProperty ShowLabelProperty =
        BindableProperty.Create(nameof(ShowLabel), typeof(bool), typeof(CapacityBar), true,
            propertyChanged: static (bindable, _, value) =>
                ((CapacityBar)bindable).CapacityLabel.IsVisible = (bool)value);
    public static readonly BindableProperty WarnThresholdProperty =
        BindableProperty.Create(nameof(WarnThreshold), typeof(double), typeof(CapacityBar), 1d,
            propertyChanged: OnValueChanged);

    public CapacityBar()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Refresh();
    }

    public int Current { get => (int)GetValue(CurrentProperty); set => SetValue(CurrentProperty, value); }
    public int Max { get => (int)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public bool ShowLabel { get => (bool)GetValue(ShowLabelProperty); set => SetValue(ShowLabelProperty, value); }
    public double WarnThreshold { get => (double)GetValue(WarnThresholdProperty); set => SetValue(WarnThresholdProperty, value); }

    private static void OnValueChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((CapacityBar)bindable).Refresh();

    private void Refresh()
    {
        var ratio = Max <= 0 ? 0 : Math.Clamp((double)Current / Max, 0, 1);
        Fill.WidthRequest = Math.Max(0, Width * ratio);
        CapacityLabel.Text = $"{Current} / {Max} going";
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }
        var isWarning = ratio >= WarnThreshold;
        Fill.SetAppThemeColor(BackgroundColorProperty,
            (Color)resources[isWarning ? "DangerLight" : "BrandGreenLight"],
            (Color)resources[isWarning ? "DangerDark" : "BrandGreenDarkTheme"]);
        SemanticProperties.SetDescription(this, $"{Current} of {Max} places filled");
    }
}
