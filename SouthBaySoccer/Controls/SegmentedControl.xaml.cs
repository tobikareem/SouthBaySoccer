using System.Collections;
using System.Reflection;
using System.Windows.Input;

namespace SouthBaySoccer.Controls;

public partial class SegmentedControl
{
    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(SegmentedControl),
            propertyChanged: OnItemsChanged);
    public static readonly BindableProperty DisplayMemberProperty =
        BindableProperty.Create(nameof(DisplayMember), typeof(string), typeof(SegmentedControl), null,
            propertyChanged: OnItemsChanged);
    public static readonly BindableProperty SelectedIndexProperty =
        BindableProperty.Create(nameof(SelectedIndex), typeof(int), typeof(SegmentedControl), 0,
            BindingMode.TwoWay, propertyChanged: OnSelectionChanged);
    public static readonly BindableProperty SelectedItemProperty =
        BindableProperty.Create(nameof(SelectedItem), typeof(object), typeof(SegmentedControl), null,
            BindingMode.TwoWay);
    public static readonly BindableProperty SelectionChangedCommandProperty =
        BindableProperty.Create(nameof(SelectionChangedCommand), typeof(ICommand), typeof(SegmentedControl));

    private readonly List<object> _items = [];

    public SegmentedControl() => InitializeComponent();

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public string? DisplayMember { get => (string?)GetValue(DisplayMemberProperty); set => SetValue(DisplayMemberProperty, value); }
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public ICommand? SelectionChangedCommand { get => (ICommand?)GetValue(SelectionChangedCommandProperty); set => SetValue(SelectionChangedCommandProperty, value); }

    private static void OnItemsChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((SegmentedControl)bindable).Render();

    private static void OnSelectionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (SegmentedControl)bindable;
        control.UpdateSelection();

        // UpdateSelection leaves SelectedItem null when there is nothing to select — the segments
        // are rebuilt from scratch whenever ItemsSource is re-raised, so an index change can land
        // in that window. Handing null to a typed RelayCommand<T> throws inside the command, which
        // surfaces as a crash rather than a binding warning, so there is no selection to report.
        if (control.SelectedItem is null)
        {
            return;
        }

        control.SelectionChangedCommand?.Execute(control.SelectedItem);
    }

    private void Render()
    {
        Segments.Clear();
        _items.Clear();
        if (ItemsSource is null)
        {
            return;
        }

        foreach (var item in ItemsSource)
        {
            if (item is null)
            {
                continue;
            }
            var index = _items.Count;
            _items.Add(item);
            var button = new Button
            {
                Text = GetDisplayText(item),
                Command = new Command(() => SelectedIndex = index),
                MinimumHeightRequest = (double)Application.Current!.Resources["TouchMin"],
                Padding = (Thickness)Application.Current.Resources["SegmentPadding"],
                FontFamily = "InterSemibold",
                FontSize = (double)Application.Current.Resources["FontLabel"],
                CornerRadius = Convert.ToInt32(Application.Current.Resources["RadiusSegment"]),
                BorderWidth = 0
            };
            FlexLayout.SetGrow(button, 1);
            SemanticProperties.SetDescription(button, $"Select {button.Text}");
            Segments.Add(button);
        }
        UpdateSelection();
    }

    private string GetDisplayText(object item)
    {
        if (string.IsNullOrWhiteSpace(DisplayMember))
        {
            return item.ToString() ?? string.Empty;
        }
        return item.GetType().GetProperty(DisplayMember, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(item)?.ToString() ?? string.Empty;
    }

    private void UpdateSelection()
    {
        if (_items.Count == 0)
        {
            SelectedItem = null;
            return;
        }
        var selected = Math.Clamp(SelectedIndex, 0, _items.Count - 1);
        if (selected != SelectedIndex)
        {
            SelectedIndex = selected;
            return;
        }
        SelectedItem = _items[selected];
        var resources = Application.Current!.Resources;
        for (var index = 0; index < Segments.Count; index++)
        {
            if (Segments[index] is not Button button)
            {
                continue;
            }
            var isSelected = index == selected;
            SemanticProperties.SetDescription(
                button,
                isSelected ? $"{button.Text}, selected" : $"Select {button.Text}");
            button.SetAppThemeColor(BackgroundColorProperty,
                (Color)resources[isSelected ? "SurfaceLight" : "BrandMistLight"],
                (Color)resources[isSelected ? "SurfaceDark" : "BrandMistDark"]);
            button.SetAppThemeColor(Button.TextColorProperty,
                (Color)resources[isSelected ? "BrandGreenLight" : "BrandSageLight"],
                (Color)resources[isSelected ? "BrandGreenDarkTheme" : "BrandSageDark"]);
        }
    }
}
