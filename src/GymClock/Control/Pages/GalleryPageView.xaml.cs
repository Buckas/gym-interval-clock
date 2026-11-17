using System.Windows.Controls;

namespace GymClock.Wpf
{
    public partial class GalleryPageView : UserControl
    {
        private bool _loading = true;

        public GalleryPageView()
        {
            InitializeComponent();
            IconList.ItemsSource = Icons.All;
            ThemePicker.SelectedIndex = ThemeService.Choice == "light" ? 1 : ThemeService.Choice == "dark" ? 2 : 0;
            _loading = false;
        }

        private void ThemePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            ListBoxItem item = ThemePicker.SelectedItem as ListBoxItem;
            if (item != null) ThemeService.Apply(item.Tag as string);
        }
    }
}
