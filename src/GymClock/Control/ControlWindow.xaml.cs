using System.Windows;
using System.Windows.Input;

namespace GymClock.Wpf
{
    /// <summary>
    /// The Control window: a navigation rail and the page showing. Where it opens, and whether it
    /// is a full window or a side panel, is <see cref="ControlHost"/>'s job.
    /// </summary>
    public partial class ControlWindow : Window
    {
        public ControlWindow(ControlViewModel model)
        {
            InitializeComponent();
            Model = model;
            DataContext = model;
        }

        public ControlViewModel Model { get; }

        /// <summary>A short message in the bar at the top of the page (IDialogService.Notify).</summary>
        public void Notify(string message, InfoKind kind)
        {
            InfoBar.Show(message, kind);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // Esc closes the side panel, so the clock is back in one key.
            if (e.Key == Key.Escape && Model.IsPanel)
            {
                e.Handled = true;
                Close();
                return;
            }
            base.OnPreviewKeyDown(e);
        }
    }
}
