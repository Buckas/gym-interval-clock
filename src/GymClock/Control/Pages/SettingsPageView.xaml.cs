using System;
using System.Windows.Controls;

namespace GymClock.Wpf
{
    public partial class SettingsPageView : UserControl
    {
        /// <param name="reload">Gives the settings to show each time the page comes on screen (null: keep what it has).</param>
        public SettingsPageView(SettingsViewModel model, Func<TimerSettings> reload = null)
        {
            InitializeComponent();
            DataContext = model;
            Loaded += delegate { if (reload != null) model.Load(reload()); };
            Unloaded += delegate { model.Flush(); };
        }
    }
}
