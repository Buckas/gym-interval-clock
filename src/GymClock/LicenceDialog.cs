using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GymClock
{
    /// <summary>
    /// Shown at startup when the app may not run, and on demand with F3 as an About
    /// box. In blocking mode the only ways out are a valid licence or Exit.
    ///
    /// Layout lives in LicenceDialog.Designer.cs and is editable in the Visual Studio
    /// designer. Only the status wording, which depends on the licence state, and the
    /// blocking-vs-about differences are applied here at runtime.
    /// </summary>
    public partial class LicenceDialog : Form
    {
        private bool _blocking;
        private LicenceStatus _status;

        /// <summary>Design-time constructor. The designer needs a parameterless one.</summary>
        public LicenceDialog()
        {
            InitializeComponent();
        }

        public LicenceDialog(LicenceStatus status, bool blocking)
            : this()
        {
            _status = status;
            _blocking = blocking;

            _title.Text = Licensing.ProductName + "  v" + ShortProductVersion();
            _copyrightLabel.Text = Licensing.Copyright;
            _machineBox.Text = Licensing.MachineId();
            _siteLink.Text = OnlineServices.BaseUrl;

            _followUpLabel.Text =
                "Once approved, press \"Check online\" and the key will install itself."
                + Environment.NewLine + "Enquiries: " + Licensing.ContactDetails;

            // Blocking mode: no way past this except a licence or quitting.
            Text = blocking ? "Licence required" : "About " + Licensing.ProductName;
            ShowInTaskbar = blocking;
            StartPosition = blocking ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
            _closeButton.Text = blocking ? "Exit" : "Close";
            _closeButton.DialogResult = blocking ? DialogResult.Cancel : DialogResult.OK;

            UpdateStatusText();
        }

        /// <summary>
        /// Application.ProductVersion includes the SourceRevisionId (a full git
        /// commit hash) appended after a '+', e.g. "2.0.0+abcdef1234...". That's
        /// too wide for the title label and not meaningful to a user, so only
        /// the plain "major.minor.patch" part is shown here.
        /// </summary>
        private static string ShortProductVersion()
        {
            string version = Application.ProductVersion ?? string.Empty;
            int plus = version.IndexOf('+');
            return plus >= 0 ? version.Substring(0, plus) : version;
        }

        private void UpdateStatusText()
        {            string text;

            switch (_status.State)
            {
                case LicenceState.Licensed:
                    text = "Licensed to " + _status.Licence.Name + "." + Environment.NewLine
                         + (_status.Licence.Expires.HasValue
                             ? "This licence expires on " + _status.Licence.Expires.Value.ToString("d MMMM yyyy", CultureInfo.CurrentCulture) + "."
                             : "This licence does not expire.")
                         + (_status.Licence.IsMachineLocked ? Environment.NewLine + "Locked to this computer." : string.Empty);
                    _statusLabel.ForeColor = Color.FromArgb(15, 110, 60);
                    break;

                case LicenceState.TrialActive:
                case LicenceState.TrialEnding:
                    text = "No licence installed. This copy may be used without a licence key up to"
                         + Environment.NewLine + "and including "
                         + Licensing.FreeUseEndsOn.ToString("d MMMM yyyy", CultureInfo.CurrentCulture)
                         + string.Format(CultureInfo.CurrentCulture, " - {0} day{1} remaining.",
                             _status.DaysRemaining, _status.DaysRemaining == 1 ? string.Empty : "s")
                         + Environment.NewLine + "From "
                         + Licensing.FreeUseEndsOn.AddDays(1).ToString("d MMMM yyyy", CultureInfo.CurrentCulture)
                         + " a licence key will be required to start the app.";
                    _statusLabel.ForeColor = _status.State == LicenceState.TrialEnding
                        ? Color.FromArgb(170, 90, 10) : Color.Black;
                    break;

                case LicenceState.LicenceGrace:
                    text = "Licensed to " + _status.Licence.Name + ", but the licence expired on "
                         + _status.Licence.Expires.Value.ToString("d MMMM yyyy", CultureInfo.CurrentCulture) + "."
                         + Environment.NewLine
                         + string.Format(CultureInfo.CurrentCulture,
                             "The app will keep working for another {0} day{1} while a renewal is arranged.",
                             _status.DaysRemaining, _status.DaysRemaining == 1 ? string.Empty : "s")
                         + Environment.NewLine + "Request a renewal below, then press \"Check online\".";
                    _statusLabel.ForeColor = Color.FromArgb(170, 90, 10);
                    break;

                case LicenceState.TrialExpired:
                    text = "This app has required a licence key since "
                         + Licensing.FreeUseEndsOn.AddDays(1).ToString("d MMMM yyyy", CultureInfo.CurrentCulture) + "."
                         + Environment.NewLine + "Enter a licence key below to continue using the app."
                         + (string.IsNullOrEmpty(_status.Detail) ? string.Empty : Environment.NewLine + _status.Detail);
                    _statusLabel.ForeColor = Color.FromArgb(160, 30, 25);
                    break;

                case LicenceState.LicenceExpired:
                    text = "The installed licence has expired." + Environment.NewLine + _status.Detail
                         + Environment.NewLine + "Enter a renewed licence key below.";
                    _statusLabel.ForeColor = Color.FromArgb(160, 30, 25);
                    break;

                case LicenceState.ClockRolledBack:
                    text = "The system clock appears to have been set back to before the licence"
                         + Environment.NewLine + "cutoff date. Correct the date and time, then restart the app.";
                    _statusLabel.ForeColor = Color.FromArgb(160, 30, 25);
                    break;

                default:
                    text = "This copy of the app was not packaged correctly and cannot be licensed."
                         + Environment.NewLine + "Please contact the author.";
                    _statusLabel.ForeColor = Color.FromArgb(160, 30, 25);
                    break;
            }

            _statusLabel.Text = text;
        }

        // -------------------------------------------------------------- handlers

        private void CopyMachine_Click(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(Licensing.MachineId());
            }
            catch { }
        }

        private void SiteLink_Clicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(OnlineServices.BaseUrl);
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch { }
        }

        /// <summary>
        /// Writes the support report the author can ask for if a licence question needs
        /// discussing. Deliberately a file the user saves and chooses to attach, rather
        /// than anything the app sends by itself.
        /// </summary>
        private void SaveReport_Click(object sender, EventArgs e)
        {
            DiagnosticReport.SaveInteractive(this, Settings);
        }

        /// <summary>Set by the caller so the report can include the current session setup.</summary>
        public TimerSettings Settings { get; set; }

        /// <summary>
        /// Set by the caller when a newer version has been seen. The clock display tells
        /// people to press F3, so F3 has to actually show it.
        ///
        /// Not called "Update" - that would hide the inherited Control.Update() method.
        /// </summary>
        public UpdateInfo AvailableUpdate { get; set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (AvailableUpdate == null || !AvailableUpdate.IsNewerThanThis) return;

            _updateLink.Text = AvailableUpdate.Description + " - click here to download";
            _updateLink.Visible = true;
        }

        private void UpdateLink_Clicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (AvailableUpdate == null) return;

            try
            {
                ProcessStartInfo info = new ProcessStartInfo(AvailableUpdate.DownloadUrl);
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch { }
        }

        private void Request_Click(object sender, EventArgs e)
        {
            using (LicenceRequestForm form = new LicenceRequestForm(_status))
            {
                form.ShowDialog(this);
            }
        }

        private void Activate_Click(object sender, EventArgs e)
        {
            string entered = _keyBox.Text.Trim();
            if (entered.Length == 0)
            {
                MessageBox.Show(this, "Paste the licence key into the box first.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string error;
            if (!Licensing.Install(entered, out error))
            {
                MessageBox.Show(this, "That licence key was not accepted, because " + error + ".",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Accepted();
        }

        private void FromFile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Select a licence file";
                dialog.Filter = "Licence files (*.licence;*.txt)|*.licence;*.txt|All files (*.*)|*.*";

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    _keyBox.Text = File.ReadAllText(dialog.FileName).Trim();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "That file could not be read: " + ex.Message, Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            Activate_Click(sender, e);
        }

        /// <summary>
        /// Pulls a licence that has been published for this machine. Nothing is
        /// uploaded; this is a single GET for a file named after the computer ID.
        /// </summary>
        private async void CheckOnline_Click(object sender, EventArgs e)
        {
            _checkOnlineButton.Enabled = false;
            _checkOnlineButton.Text = "Checking...";
            Cursor = Cursors.WaitCursor;

            bool installed = false;
            try
            {
                installed = await OnlineServices.TryFetchLicenceAsync();
            }
            catch { }

            Cursor = Cursors.Default;
            _checkOnlineButton.Enabled = true;
            _checkOnlineButton.Text = "Check online";

            if (!installed)
            {
                MessageBox.Show(this,
                    "No licence has been published for this computer yet." + Environment.NewLine
                    + Environment.NewLine
                    + "If you have only just applied, give it a little time and try again. "
                    + "If your school network blocks the site, ask for the key by email and "
                    + "paste it into the box above instead.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Accepted();
        }

        private void Accepted()
        {
            _status = Licensing.Current;
            UpdateStatusText();

            MessageBox.Show(this, "Thank you - this copy is now licensed to "
                + _status.Licence.Name + ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // In blocking mode Escape must not look like a way to skip the check.
            if (e.KeyCode == Keys.Escape && !_blocking)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
