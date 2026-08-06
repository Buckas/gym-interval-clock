using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace GymClock
{
    /// <summary>
    /// Collects the few details needed to issue a licence and hands them to the
    /// user's own mail client as a pre-filled message.
    ///
    /// The app never sends anything itself. Doing so would require mail credentials
    /// or an API key compiled into a program that is handed out freely - which is no
    /// secret at all, and turns the app into someone else's spam relay. This way the
    /// user sees exactly what is being sent and presses Send themselves.
    ///
    /// Layout lives in LicenceRequestForm.Designer.cs and is editable in the
    /// Visual Studio designer. Only wording that depends on the licence state, and
    /// the live preview, are set here at runtime.
    /// </summary>
    public partial class LicenceRequestForm : Form
    {
        private readonly LicenceStatus _status;

        /// <summary>Design-time constructor. The designer needs a parameterless one.</summary>
        public LicenceRequestForm()
        {
            InitializeComponent();
        }

        public LicenceRequestForm(LicenceStatus status)
            : this()
        {
            _status = status;

            _heading.Text = HeadingText();

            RequesterDetails saved = RequesterDetails.Load();
            _name.Text = saved.Name;
            _school.Text = saved.School;
            _email.Text = saved.Email;

            bool renewing = _status != null
                && (_status.State == LicenceState.Licensed || _status.State == LicenceState.LicenceGrace);
            _which.SelectedIndex = renewing ? 3 : 0;

            _name.TextChanged += Field_Changed;
            _school.TextChanged += Field_Changed;
            _email.TextChanged += Field_Changed;
            _note.TextChanged += Field_Changed;
            _which.SelectedIndexChanged += Field_Changed;

            UpdatePreview();
        }

        private void Field_Changed(object sender, EventArgs e)
        {
            UpdatePreview();
        }

        private string HeadingText()
        {
            if (_status == null) return "This copy needs a licence key";

            switch (_status.State)
            {
                case LicenceState.Licensed:
                    return string.Format(CultureInfo.CurrentCulture,
                        "Your licence expires in {0} day{1}", _status.DaysRemaining,
                        _status.DaysRemaining == 1 ? string.Empty : "s");

                case LicenceState.LicenceGrace:
                    return "Your licence has expired - renewal needed";

                case LicenceState.TrialEnding:
                    return string.Format(CultureInfo.CurrentCulture,
                        "A licence key will be needed in {0} day{1}", _status.DaysRemaining,
                        _status.DaysRemaining == 1 ? string.Empty : "s");

                default:
                    return "This copy needs a licence key";
            }
        }

        // ----------------------------------------------------------- the message

        private string Subject()
        {
            return "Gym Clock licence request - " + Licensing.MachineId();
        }

        private string Body()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Gym Interval Clock - licence request");
            sb.AppendLine();
            sb.AppendLine("Name         : " + _name.Text.Trim());
            sb.AppendLine("School       : " + _school.Text.Trim());
            sb.AppendLine("Email        : " + _email.Text.Trim());
            sb.AppendLine("Request type : " + (_which.SelectedItem == null
                ? string.Empty : _which.SelectedItem.ToString()));
            sb.AppendLine();
            sb.AppendLine("Computer ID  : " + Licensing.MachineId());
            sb.AppendLine("App version  : " + Application.ProductVersion);

            if (_status != null && _status.Licence != null && !string.IsNullOrEmpty(_status.Licence.Name))
            {
                sb.AppendLine("Current key  : " + _status.Licence.Name);
                if (_status.Licence.Expires.HasValue)
                {
                    sb.AppendLine("Expires      : " + _status.Licence.Expires.Value
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }
            }

            sb.AppendLine();
            sb.AppendLine(UsageLog.Summary());

            UsageSnapshot usage = UsageLog.Snapshot();
            UsageAssessment.Result assessment = UsageAssessment.Evaluate(usage);

            sb.AppendLine("Licence type : " + assessment.Headline);
            if (assessment.Reasons.Count > 0)
            {
                sb.AppendLine("Because      : " + string.Join("; ", assessment.Reasons.ToArray()));
            }

            foreach (string flag in assessment.Flags)
            {
                sb.AppendLine("Flag         : " + flag);
            }

            string code = UsageAssessment.Code(usage, assessment);
            if (code.Length > 0)
            {
                sb.AppendLine("Usage code   : " + code);
            }

            string note = _note.Text.Trim();
            if (note.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Note         : " + note);
            }

            return sb.ToString();
        }

        private void UpdatePreview()
        {
            _preview.Text = "To: " + Licensing.ContactDetails + Environment.NewLine
                + "Subject: " + Subject() + Environment.NewLine
                + Environment.NewLine
                + Body();
        }

        // ------------------------------------------------------------ the actions

        private bool IsComplete(out string problem)
        {
            problem = null;

            if (_name.Text.Trim().Length < 2) problem = "Please enter your name.";
            else if (_school.Text.Trim().Length < 2) problem = "Please enter your school or organisation.";
            else if (!_email.Text.Contains("@") || _email.Text.Trim().Length < 5)
                problem = "Please enter an email address so a reply can be sent.";

            return problem == null;
        }

        private void Remember()
        {
            RequesterDetails details = new RequesterDetails();
            details.Name = _name.Text.Trim();
            details.School = _school.Text.Trim();
            details.Email = _email.Text.Trim();
            details.Save();
        }

        private void Send_Click(object sender, EventArgs e)
        {
            string problem;
            if (!IsComplete(out problem))
            {
                MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Remember();
            Licensing.RecordRenewalPrompt(14);   // a fortnight before asking again

            string url = "mailto:" + Uri.EscapeDataString(Licensing.ContactDetails)
                + "?subject=" + Uri.EscapeDataString(Subject())
                + "&body=" + Uri.EscapeDataString(Body());

            if (!Open(url))
            {
                MessageBox.Show(this,
                    "No email program could be opened on this computer." + Environment.NewLine
                    + Environment.NewLine
                    + "Use \"Copy details\" instead and paste them into an email, or Teams, "
                    + "or the web page.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(this,
                "Your email program should now be open with the request filled in."
                + Environment.NewLine + Environment.NewLine
                + "Press Send there - it will not go until you do." + Environment.NewLine
                + Environment.NewLine
                + "Once you get a reply, press F3 in the app and click \"Check online\".",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }

        private void Copy_Click(object sender, EventArgs e)
        {
            Remember();

            try
            {
                Clipboard.SetText(_preview.Text);
                MessageBox.Show(this, "Copied. Paste it into an email or a Teams message to "
                    + Licensing.ContactDetails + ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
                MessageBox.Show(this, "The clipboard could not be used. Select the text in the "
                    + "box and copy it manually.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Web_Click(object sender, EventArgs e)
        {
            Open(OnlineServices.BaseUrl);
        }

        private static bool Open(string url)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(url);
                info.UseShellExecute = true;
                Process.Start(info);
                return true;
            }
            catch
            {
                return false;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }
    }
}
