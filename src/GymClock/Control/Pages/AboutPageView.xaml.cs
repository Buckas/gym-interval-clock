using System.Windows.Controls;

namespace GymClock.Wpf
{
    public partial class AboutPageView : UserControl
    {
        private readonly AboutViewModel _model;

        public AboutPageView(AboutViewModel model)
        {
            InitializeComponent();
            _model = model;
            DataContext = model;
            Loaded += delegate { model.Refresh(); };
        }

        /// <summary>Opens the request form and scrolls to it (the renewal offer at start-up).</summary>
        public void ShowRequest()
        {
            _model.ShowRequest = true;
            Dispatcher.BeginInvoke(new System.Action(() => RequestCard.BringIntoView()), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
