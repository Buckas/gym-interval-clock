using System.ComponentModel;
using System.Windows.Controls;

namespace GymClock.Wpf
{
    public partial class LinkPageView : UserControl
    {
        public LinkPageView(LinkViewModel model)
        {
            InitializeComponent();
            DataContext = model;
            Loaded += delegate
            {
                model.Start();
                if (model.IsFollow && model.Code.Length == 0) CodeBox.Focus();
            };
            Unloaded += delegate { model.Stop(); };
            model.PropertyChanged += (s, e) =>
            {
                // On to the second-screen page: straight into the code box.
                if (e.PropertyName == nameof(LinkViewModel.IsFollow) && model.IsFollow && IsLoaded) CodeBox.Focus();
            };
        }
    }
}
