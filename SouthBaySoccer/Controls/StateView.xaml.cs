using System.Windows.Input;

namespace SouthBaySoccer.Controls;

[ContentProperty(nameof(Body))]
public partial class StateView
{
    public static readonly BindableProperty StateProperty =
        BindableProperty.Create(nameof(State), typeof(ViewState), typeof(StateView), ViewState.Content,
            propertyChanged: static (bindable, _, _) => ((StateView)bindable).Refresh());
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(StateView), string.Empty);
    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(nameof(Message), typeof(string), typeof(StateView), string.Empty);
    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(nameof(Glyph), typeof(string), typeof(StateView), string.Empty);
    public static readonly BindableProperty GlyphFontFamilyProperty =
        BindableProperty.Create(
            nameof(GlyphFontFamily),
            typeof(string),
            typeof(StateView),
            "InterRegular");
    public static readonly BindableProperty RetryCommandProperty =
        BindableProperty.Create(nameof(RetryCommand), typeof(ICommand), typeof(StateView), null,
            propertyChanged: static (bindable, _, _) => ((StateView)bindable).Refresh());
    public static readonly BindableProperty BodyProperty =
        BindableProperty.Create(
            nameof(Body),
            typeof(View),
            typeof(StateView),
            propertyChanged: static (bindable, _, value) =>
                ((StateView)bindable).ApplyBody((View?)value));

    public StateView()
    {
        InitializeComponent();
        Refresh();
    }

    public ViewState State { get => (ViewState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string GlyphFontFamily { get => (string)GetValue(GlyphFontFamilyProperty); set => SetValue(GlyphFontFamilyProperty, value); }
    public ICommand? RetryCommand { get => (ICommand?)GetValue(RetryCommandProperty); set => SetValue(RetryCommandProperty, value); }
    public View? Body { get => (View?)GetValue(BodyProperty); set => SetValue(BodyProperty, value); }

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
        ContentPresenter.Content = body;
        if (body is not null)
        {
            SetInheritedBindingContext(body, BindingContext);
        }
    }

    private void Refresh()
    {
        var showsContent = State == ViewState.Content;
        ContentPresenter.IsVisible = showsContent;
        StatePanel.IsVisible = !showsContent;
        LoadingIndicator.IsVisible = State == ViewState.Loading;
        LoadingIndicator.IsRunning = State == ViewState.Loading;
        RetryButton.IsVisible = RetryCommand is not null && State is ViewState.Error or ViewState.Offline;
        SemanticProperties.SetDescription(this, showsContent ? "Content" : $"{State}: {Title}. {Message}");
    }
}
