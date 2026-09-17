using System.Windows.Input;

namespace SouthBaySoccer.Controls;

/// <summary>
/// The prefilled-message card plus WhatsApp button used by every handoff screen (sign-up start,
/// sign-in verify). Shows the exact text the app will place in WhatsApp; nothing sends until the
/// player taps inside WhatsApp.
/// </summary>
public partial class WhatsAppHandoffCard : ContentView
{
    public static readonly BindableProperty LabelProperty =
        BindableProperty.Create(nameof(Label), typeof(string), typeof(WhatsAppHandoffCard), "Message we'll send to the bot");
    public static readonly BindableProperty MessageTextProperty =
        BindableProperty.Create(nameof(MessageText), typeof(string), typeof(WhatsAppHandoffCard), string.Empty);
    public static readonly BindableProperty HintProperty =
        BindableProperty.Create(nameof(Hint), typeof(string), typeof(WhatsAppHandoffCard), string.Empty);
    public static readonly BindableProperty ButtonTextProperty =
        BindableProperty.Create(nameof(ButtonText), typeof(string), typeof(WhatsAppHandoffCard), "Continue with WhatsApp");
    public static readonly BindableProperty ContinueCommandProperty =
        BindableProperty.Create(nameof(ContinueCommand), typeof(ICommand), typeof(WhatsAppHandoffCard));
    public static readonly BindableProperty IsBusyProperty =
        BindableProperty.Create(nameof(IsBusy), typeof(bool), typeof(WhatsAppHandoffCard), false,
            propertyChanged: (bindable, _, _) => ((WhatsAppHandoffCard)bindable).OnPropertyChanged(nameof(IsNotBusy)));

    public WhatsAppHandoffCard() => InitializeComponent();

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string MessageText { get => (string)GetValue(MessageTextProperty); set => SetValue(MessageTextProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public string ButtonText { get => (string)GetValue(ButtonTextProperty); set => SetValue(ButtonTextProperty, value); }
    public ICommand? ContinueCommand { get => (ICommand?)GetValue(ContinueCommandProperty); set => SetValue(ContinueCommandProperty, value); }
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
    public bool IsNotBusy => !IsBusy;
}
