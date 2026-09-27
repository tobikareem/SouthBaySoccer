namespace SouthBaySoccer.Controls;

/// <summary>Countdown card for a bot link's 15-minute lifetime. Purely presentational; the page model ticks it.</summary>
public partial class LinkExpiryCard : ContentView
{
    public static readonly BindableProperty RemainingTextProperty =
        BindableProperty.Create(nameof(RemainingText), typeof(string), typeof(LinkExpiryCard), "15:00");
    public static readonly BindableProperty RemainingPercentProperty =
        BindableProperty.Create(nameof(RemainingPercent), typeof(int), typeof(LinkExpiryCard), 100,
            propertyChanged: (bindable, _, _) =>
            {
                var card = (LinkExpiryCard)bindable;
                card.OnPropertyChanged(nameof(BadgeVariant));
                card.OnPropertyChanged(nameof(Progress));
            });
    public static readonly BindableProperty NoteProperty =
        BindableProperty.Create(nameof(Note), typeof(string), typeof(LinkExpiryCard), string.Empty);

    public LinkExpiryCard() => InitializeComponent();

    public string RemainingText { get => (string)GetValue(RemainingTextProperty); set => SetValue(RemainingTextProperty, value); }
    public int RemainingPercent { get => (int)GetValue(RemainingPercentProperty); set => SetValue(RemainingPercentProperty, value); }
    public string Note { get => (string)GetValue(NoteProperty); set => SetValue(NoteProperty, value); }

    /// <summary>Remaining lifetime as a 0–1 fraction for the bar.</summary>
    public double Progress => RemainingPercent / 100d;

    /// <summary>Amber while time remains, red once the link is dead.</summary>
    public BadgeVariant BadgeVariant => RemainingPercent > 0 ? BadgeVariant.Warning : BadgeVariant.Danger;
}
