using System.Windows.Input;

namespace SouthBaySoccer.Controls;

public partial class CounterStepper
{
    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(int), typeof(CounterStepper), 0, BindingMode.TwoWay,
            propertyChanged: static (bindable, _, _) => ((CounterStepper)bindable).RefreshButtons());
    public static readonly BindableProperty MinimumProperty =
        BindableProperty.Create(nameof(Minimum), typeof(int), typeof(CounterStepper), 0,
            propertyChanged: static (bindable, _, _) => ((CounterStepper)bindable).RefreshButtons());
    public static readonly BindableProperty MaximumProperty =
        BindableProperty.Create(nameof(Maximum), typeof(int), typeof(CounterStepper), 99,
            propertyChanged: static (bindable, _, _) => ((CounterStepper)bindable).RefreshButtons());
    public static readonly BindableProperty StepProperty =
        BindableProperty.Create(nameof(Step), typeof(int), typeof(CounterStepper), 1);
    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(nameof(Glyph), typeof(string), typeof(CounterStepper), null);
    public static readonly BindableProperty GlyphFontFamilyProperty =
        BindableProperty.Create(nameof(GlyphFontFamily), typeof(string), typeof(CounterStepper), null);
    public static readonly BindableProperty CaptionProperty =
        BindableProperty.Create(nameof(Caption), typeof(string), typeof(CounterStepper), null,
            propertyChanged: static (bindable, _, _) => ((CounterStepper)bindable).RefreshButtons());
    public static readonly BindableProperty SemanticCaptionProperty =
        BindableProperty.Create(nameof(SemanticCaption), typeof(string), typeof(CounterStepper), null,
            propertyChanged: static (bindable, _, _) => ((CounterStepper)bindable).RefreshButtons());
    public static readonly BindableProperty ValueChangedCommandProperty =
        BindableProperty.Create(nameof(ValueChangedCommand), typeof(ICommand), typeof(CounterStepper));

    public CounterStepper()
    {
        InitializeComponent();
        RefreshButtons();
    }

    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Step { get => (int)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string? GlyphFontFamily { get => (string?)GetValue(GlyphFontFamilyProperty); set => SetValue(GlyphFontFamilyProperty, value); }
    public string? Caption { get => (string?)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }

    /// <summary>
    /// Disambiguates identical steppers for screen readers (e.g. "Team Green wins" when three teams
    /// each show a "Wins" stepper). Falls back to <see cref="Caption"/> when unset.
    /// </summary>
    public string? SemanticCaption { get => (string?)GetValue(SemanticCaptionProperty); set => SetValue(SemanticCaptionProperty, value); }
    public ICommand? ValueChangedCommand { get => (ICommand?)GetValue(ValueChangedCommandProperty); set => SetValue(ValueChangedCommandProperty, value); }

    private void DecrementClicked(object? sender, EventArgs e) => ChangeValue(-Step);
    private void IncrementClicked(object? sender, EventArgs e) => ChangeValue(Step);

    private void ChangeValue(int delta)
    {
        Value = Math.Clamp(Value + delta, Minimum, Maximum);
        ValueChangedCommand?.Execute(Value);
    }

    private void RefreshButtons()
    {
        DecrementButton.IsEnabled = Value > Minimum;
        IncrementButton.IsEnabled = Value < Maximum;
        var caption = string.IsNullOrWhiteSpace(SemanticCaption) ? Caption : SemanticCaption;
        SemanticProperties.SetDescription(this, $"{caption}: {Value}");
        SemanticProperties.SetDescription(DecrementButton, $"Decrease {caption}");
        SemanticProperties.SetDescription(IncrementButton, $"Increase {caption}");
    }
}
