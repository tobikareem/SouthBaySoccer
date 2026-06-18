using SouthBaySoccer.Models;
using SouthBaySoccer.PageModels;

namespace SouthBaySoccer.Pages
{
    public partial class MainPage : ContentPage
    {
        public MainPage(MainPageModel model)
        {
            InitializeComponent();
            BindingContext = model;
        }
    }
}