using System.Windows.Controls;

namespace GymClock.Wpf
{
    public partial class SessionPageView : UserControl
    {
        public SessionPageView(SessionViewModel model)
        {
            InitializeComponent();
            DataContext = model;
            // Refresh only while the page is on screen.
            Loaded += delegate { model.Start(); };
            Unloaded += delegate { model.Stop(); };
        }
    }
}
