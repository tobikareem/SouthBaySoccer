using System.Windows.Input;

namespace SouthBaySoccer.Controls;

public partial class AnnouncementCard : ContentView
{
    public static readonly BindableProperty AuthorNameProperty = BindableProperty.Create(nameof(AuthorName), typeof(string), typeof(AnnouncementCard), string.Empty);
    public static readonly BindableProperty AuthorInitialsProperty = BindableProperty.Create(nameof(AuthorInitials), typeof(string), typeof(AnnouncementCard), string.Empty);
    public static readonly BindableProperty GroupNameProperty = BindableProperty.Create(nameof(GroupName), typeof(string), typeof(AnnouncementCard), string.Empty);
    public static readonly BindableProperty TimeLabelProperty = BindableProperty.Create(nameof(TimeLabel), typeof(string), typeof(AnnouncementCard), string.Empty);
    public static readonly BindableProperty BodyProperty = BindableProperty.Create(nameof(Body), typeof(string), typeof(AnnouncementCard), string.Empty);
    public static readonly BindableProperty IsUnreadProperty = BindableProperty.Create(nameof(IsUnread), typeof(bool), typeof(AnnouncementCard));
    public static readonly BindableProperty ContextChipTextProperty = BindableProperty.Create(nameof(ContextChipText), typeof(string), typeof(AnnouncementCard), string.Empty, propertyChanged: OnContextChanged);
    public static readonly BindableProperty ContextChipGlyphProperty = BindableProperty.Create(nameof(ContextChipGlyph), typeof(string), typeof(AnnouncementCard), string.Empty);
    public static readonly BindableProperty ContextCommandProperty = BindableProperty.Create(nameof(ContextCommand), typeof(ICommand), typeof(AnnouncementCard), propertyChanged: OnContextChanged);
    public static readonly BindableProperty ContextTextProperty = BindableProperty.Create(nameof(ContextText), typeof(string), typeof(AnnouncementCard), string.Empty, propertyChanged: OnContextChanged);
    public static readonly BindableProperty HasContextProperty = BindableProperty.Create(nameof(HasContext), typeof(bool), typeof(AnnouncementCard));

    public AnnouncementCard() => InitializeComponent();

    public string AuthorName { get => (string)GetValue(AuthorNameProperty); set => SetValue(AuthorNameProperty, value); }
    public string AuthorInitials { get => (string)GetValue(AuthorInitialsProperty); set => SetValue(AuthorInitialsProperty, value); }
    public string GroupName { get => (string)GetValue(GroupNameProperty); set => SetValue(GroupNameProperty, value); }
    public string TimeLabel { get => (string)GetValue(TimeLabelProperty); set => SetValue(TimeLabelProperty, value); }
    public string Body { get => (string)GetValue(BodyProperty); set => SetValue(BodyProperty, value); }
    public bool IsUnread { get => (bool)GetValue(IsUnreadProperty); set => SetValue(IsUnreadProperty, value); }
    public string ContextChipText { get => (string)GetValue(ContextChipTextProperty); set => SetValue(ContextChipTextProperty, value); }
    public string ContextChipGlyph { get => (string)GetValue(ContextChipGlyphProperty); set => SetValue(ContextChipGlyphProperty, value); }
    public ICommand? ContextCommand { get => (ICommand?)GetValue(ContextCommandProperty); set => SetValue(ContextCommandProperty, value); }
    public string ContextText { get => (string)GetValue(ContextTextProperty); set => SetValue(ContextTextProperty, value); }
    public bool HasContext { get => (bool)GetValue(HasContextProperty); private set => SetValue(HasContextProperty, value); }

    private static void OnContextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var card = (AnnouncementCard)bindable;
        card.HasContext = !string.IsNullOrWhiteSpace(card.ContextChipText)
            || !string.IsNullOrWhiteSpace(card.ContextText)
            || card.ContextCommand is not null;
    }
}
