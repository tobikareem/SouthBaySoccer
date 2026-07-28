namespace SouthBaySoccer.Controls;

public partial class GroupChoiceRow : ContentView
{
    public static readonly BindableProperty GroupNameProperty = BindableProperty.Create(nameof(GroupName), typeof(string), typeof(GroupChoiceRow), string.Empty, propertyChanged: OnDescriptionChanged);
    public static readonly BindableProperty MemberCountProperty = BindableProperty.Create(nameof(MemberCount), typeof(int), typeof(GroupChoiceRow), propertyChanged: OnDescriptionChanged);
    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(GroupChoiceRow), propertyChanged: OnDescriptionChanged);
    public static readonly BindableProperty MemberCountTextProperty = BindableProperty.Create(nameof(MemberCountText), typeof(string), typeof(GroupChoiceRow), string.Empty);
    public static readonly BindableProperty SemanticDescriptionProperty = BindableProperty.Create(nameof(SemanticDescription), typeof(string), typeof(GroupChoiceRow), string.Empty);

    public GroupChoiceRow() => InitializeComponent();
    public string GroupName { get => (string)GetValue(GroupNameProperty); set => SetValue(GroupNameProperty, value); }
    public int MemberCount { get => (int)GetValue(MemberCountProperty); set => SetValue(MemberCountProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public string MemberCountText { get => (string)GetValue(MemberCountTextProperty); private set => SetValue(MemberCountTextProperty, value); }
    public string SemanticDescription { get => (string)GetValue(SemanticDescriptionProperty); private set => SetValue(SemanticDescriptionProperty, value); }

    private static void OnDescriptionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var row = (GroupChoiceRow)bindable;
        row.MemberCountText = $"{row.MemberCount} members";
        row.SemanticDescription = $"{row.GroupName}, {row.MemberCountText}, {(row.IsSelected ? "selected" : "not selected")}";
    }
}
