using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GymClock.Wpf
{
    /// <summary>
    /// The About page (build guide 7.4): a port of LicenceDialog and LicenceRequestForm, with their
    /// wording. The same view model runs the stand-alone licence check before the clock starts
    /// (<see cref="Blocking"/>), where the only ways out are a valid licence or Exit.
    /// </summary>
    public sealed class AboutViewModel : ObservableObject
    {
        public static readonly IReadOnlyList<string> RequestKinds = new[]
        {
            "my first machine",
            "my second machine",
            "a replacement - please move a seat",
            "a renewal of an existing licence",
        };

        private readonly IDialogService _dialogs;
        private readonly Func<TimerSettings> _settings;
        private readonly Func<UpdateInfo> _update;
        private LicenceStatus _status;

        private string _statusText = string.Empty;
        private LineState _statusState = LineState.Off;
        private string _licenceKey = string.Empty;
        private bool _checking;
        private string _name = string.Empty, _school = string.Empty, _email = string.Empty, _note = string.Empty;
        private int _requestKind;
        private bool _showRequest;

        /// <param name="blocking">The licence check before the clock starts: Exit instead of Close.</param>
        /// <param name="settings">The settings for the support report (may be null).</param>
        /// <param name="update">A newer version, if one has been seen (may be null).</param>
        public AboutViewModel(IDialogService dialogs, bool blocking, Func<TimerSettings> settings, Func<UpdateInfo> update)
        {
            _dialogs = dialogs;
            _settings = settings;
            _update = update;
            Blocking = blocking;

            CopyMachineId = new RelayCommand(_ =>
            {
                if (_dialogs.CopyText(Licensing.MachineId())) _dialogs.Notify("Computer ID copied.", InfoKind.Success);
            });
            OpenSite = new RelayCommand(_ => _dialogs.Open(OnlineServices.BaseUrl));
            OpenUpdate = new RelayCommand(_ => { UpdateInfo u = _update?.Invoke(); if (u != null) _dialogs.Open(u.DownloadUrl); });
            Activate = new RelayCommand(_ => DoActivate());
            FromFile = new RelayCommand(_ => DoFromFile());
            CheckOnline = new RelayCommand(async _ => await DoCheckOnline(), _ => !_checking);
            SaveReport = new RelayCommand(_ => DoSaveReport());
            SendRequest = new RelayCommand(_ => DoSendRequest());
            CopyRequest = new RelayCommand(_ => DoCopyRequest());
            OpenWebPage = new RelayCommand(_ => _dialogs.Open(OnlineServices.BaseUrl));
            ToggleRequest = new RelayCommand(_ => ShowRequest = !ShowRequest);
            Close = new RelayCommand(_ => { CloseRequested?.Invoke(); });

            RequesterDetails saved = RequesterDetails.Load();
            _name = saved.Name ?? string.Empty;
            _school = saved.School ?? string.Empty;
            _email = saved.Email ?? string.Empty;

            Refresh();
            _requestKind = _status != null && (_status.State == LicenceState.Licensed || _status.State == LicenceState.LicenceGrace) ? 3 : 0;
        }

        public bool Blocking { get; }

        /// <summary>A licence was installed (the blocking window closes and the clock starts).</summary>
        public event Action LicenceAccepted;

        /// <summary>Close (or Exit) was pressed.</summary>
        public event Action CloseRequested;

        // ================================================================ header and status

        public string Title { get { return Licensing.ProductName + "  v" + AppVersion.Full; } }
        public string Copyright { get { return Licensing.Copyright; } }
        public string MachineId { get { return Licensing.MachineId(); } }
        public string SiteUrl { get { return OnlineServices.BaseUrl; } }
        public string CloseText { get { return Blocking ? "Exit" : "Close"; } }

        public string FollowUp
        {
            get
            {
                return "Once approved, press \"Check online\" and the key will install itself."
                    + Environment.NewLine + "Enquiries: " + Licensing.ContactDetails;
            }
        }

        public string StatusText { get { return _statusText; } private set { Set(ref _statusText, value); } }
        public LineState StatusState { get { return _statusState; } private set { Set(ref _statusState, value); } }

        public string UpdateText
        {
            get
            {
                UpdateInfo u = _update?.Invoke();
                return u != null && u.IsNewerThanThis ? u.Description + " - click here to download" : string.Empty;
            }
        }

        public bool HasUpdate { get { return UpdateText.Length > 0; } }

        /// <summary>Reads the licence state again and words it (LicenceDialog.UpdateStatusText).</summary>
        public void Refresh()
        {
            _status = Licensing.Current;
            LineState state;
            StatusText = StatusWording(_status, out state);
            StatusState = state;
            Raise(nameof(UpdateText));
            Raise(nameof(HasUpdate));
            Raise(nameof(RequestHeading));
            Raise(nameof(Preview));
        }

        /// <summary>What each licence state says, in LicenceDialog's words. Good = green, Waiting = amber, Problem = red.</summary>
        public static string StatusWording(LicenceStatus status, out LineState state)
        {
            switch (status.State)
            {
                case LicenceState.Licensed:
                    state = LineState.Good;
                    return "Licensed to " + status.Licence.Name + "." + Environment.NewLine
                         + (status.Licence.Expires.HasValue
                             ? "This licence expires on " + status.Licence.Expires.Value.ToString("d MMMM yyyy", CultureInfo.CurrentCulture) + "."
                             : "This licence does not expire.")
                         + (status.Licence.IsMachineLocked ? Environment.NewLine + "Locked to this computer." : string.Empty);

                case LicenceState.TrialActive:
                case LicenceState.TrialEnding:
                    state = status.State == LicenceState.TrialEnding ? LineState.Waiting : LineState.Off;
                    return "No licence installed. This copy may be used without a licence key up to"
                         + Environment.NewLine + "and including "
                         + Licensing.FreeUseEndsOn.ToString("d MMMM yyyy", CultureInfo.CurrentCulture)
                         + string.Format(CultureInfo.CurrentCulture, " - {0} day{1} remaining.",
                             status.DaysRemaining, status.DaysRemaining == 1 ? string.Empty : "s")
                         + Environment.NewLine + "From "
                         + Licensing.FreeUseEndsOn.AddDays(1).ToString("d MMMM yyyy", CultureInfo.CurrentCulture)
                         + " a licence key will be required to start the app.";

                case LicenceState.LicenceGrace:
                    state = LineState.Waiting;
                    return "Licensed to " + status.Licence.Name + ", but the licence expired on "
                         + status.Licence.Expires.Value.ToString("d MMMM yyyy", CultureInfo.CurrentCulture) + "."
                         + Environment.NewLine
                         + string.Format(CultureInfo.CurrentCulture,
                             "The app will keep working for another {0} day{1} while a renewal is arranged.",
                             status.DaysRemaining, status.DaysRemaining == 1 ? string.Empty : "s")
                         + Environment.NewLine + "Request a renewal below, then press \"Check online\".";

                case LicenceState.TrialExpired:
                    state = LineState.Problem;
                    return "This app has required a licence key since "
                         + Licensing.FreeUseEndsOn.AddDays(1).ToString("d MMMM yyyy", CultureInfo.CurrentCulture) + "."
                         + Environment.NewLine + "Enter a licence key below to continue using the app."
                         + (string.IsNullOrEmpty(status.Detail) ? string.Empty : Environment.NewLine + status.Detail);

                case LicenceState.LicenceExpired:
                    state = LineState.Problem;
                    return "The installed licence has expired." + Environment.NewLine + status.Detail
                         + Environment.NewLine + "Enter a renewed licence key below.";

                case LicenceState.ClockRolledBack:
                    state = LineState.Problem;
                    return "The system clock appears to have been set back to before the licence"
                         + Environment.NewLine + "cutoff date. Correct the date and time, then restart the app.";

                default:
                    state = LineState.Problem;
                    return "This copy of the app was not packaged correctly and cannot be licensed."
                         + Environment.NewLine + "Please contact the author.";
            }
        }

        // ================================================================ licence key

        public string LicenceKey { get { return _licenceKey; } set { Set(ref _licenceKey, value ?? string.Empty); } }
        public bool Checking { get { return _checking; } private set { if (Set(ref _checking, value)) { Raise(nameof(CheckOnlineText)); CommandManager.InvalidateRequerySuggested(); } } }
        public string CheckOnlineText { get { return _checking ? "Checking..." : "Check online"; } }

        public ICommand CopyMachineId { get; }
        public ICommand OpenSite { get; }
        public ICommand OpenUpdate { get; }
        public ICommand Activate { get; }
        public ICommand FromFile { get; }
        public ICommand CheckOnline { get; }
        public ICommand SaveReport { get; }
        public ICommand Close { get; }

        private void DoActivate()
        {
            string entered = _licenceKey.Trim();
            if (entered.Length == 0)
            {
                _dialogs.Info("Licence key", "Paste the licence key into the box first.");
                return;
            }

            string error;
            if (!Licensing.Install(entered, out error))
            {
                _dialogs.Info("Licence key", "That licence key was not accepted, because " + error + ".");
                return;
            }
            Accepted();
        }

        private void DoFromFile()
        {
            string path = _dialogs.PickOpenFile("Select a licence file", "Licence files (*.licence;*.txt)|*.licence;*.txt|All files (*.*)|*.*");
            if (path == null) return;
            try
            {
                LicenceKey = File.ReadAllText(path).Trim();
            }
            catch (Exception ex)
            {
                _dialogs.Info("Licence key", "That file could not be read: " + ex.Message);
                return;
            }
            DoActivate();
        }

        /// <summary>
        /// Pulls a licence that has been published for this machine. Nothing is uploaded; this is
        /// a single GET for a file named after the computer ID.
        /// </summary>
        private async Task DoCheckOnline()
        {
            Checking = true;
            bool installed = false;
            try
            {
                installed = await OnlineServices.TryFetchLicenceAsync();
            }
            catch { }
            Checking = false;

            if (!installed)
            {
                _dialogs.Info("Check online",
                    "No licence has been published for this computer yet." + Environment.NewLine
                    + Environment.NewLine
                    + "If you have only just applied, give it a little time and try again. "
                    + "If your school network blocks the site, ask for the key by email and "
                    + "paste it into the box above instead.");
                return;
            }
            Accepted();
        }

        private void Accepted()
        {
            Refresh();
            LicenceKey = string.Empty;
            _dialogs.Info("Licence installed", "Thank you - this copy is now licensed to "
                + (_status.Licence != null ? _status.Licence.Name : "you") + ".");
            LicenceAccepted?.Invoke();
        }

        /// <summary>
        /// Writes the support report the author can ask for. Deliberately a file the teacher saves
        /// and chooses to attach, rather than anything the app sends by itself.
        /// </summary>
        private void DoSaveReport()
        {
            string path = _dialogs.PickSaveFile("Save support report", "Text files (*.txt)|*.txt|All files (*.*)|*.*", DiagnosticReport.SuggestedFileName());
            if (path == null) return;

            try
            {
                File.WriteAllText(path, DiagnosticReport.Build(_settings?.Invoke()));
            }
            catch (Exception ex)
            {
                _dialogs.Info("Save support report", "The report could not be saved: " + ex.Message);
                return;
            }

            bool open = _dialogs.Confirm("Save support report",
                "Report saved to:" + Environment.NewLine + path + Environment.NewLine + Environment.NewLine
                + "Attach it to an email to " + Licensing.ContactDetails + "." + Environment.NewLine + Environment.NewLine
                + "Open the folder now?", "Open folder", "Not now");
            if (open) _dialogs.Open("explorer.exe", "/select,\"" + path + "\"");
        }

        // ================================================================ request a licence (LicenceRequestForm)

        /// <summary>The request form is folded away until asked for (or the licence needs attention).</summary>
        public bool ShowRequest { get { return _showRequest; } set { Set(ref _showRequest, value); } }
        public ICommand ToggleRequest { get; }

        public string RequestHeading
        {
            get
            {
                if (_status == null) return "This copy needs a licence key";
                switch (_status.State)
                {
                    case LicenceState.Licensed:
                        return string.Format(CultureInfo.CurrentCulture, "Your licence expires in {0} day{1}",
                            _status.DaysRemaining, _status.DaysRemaining == 1 ? string.Empty : "s");
                    case LicenceState.LicenceGrace:
                        return "Your licence has expired - renewal needed";
                    case LicenceState.TrialEnding:
                        return string.Format(CultureInfo.CurrentCulture, "A licence key will be needed in {0} day{1}",
                            _status.DaysRemaining, _status.DaysRemaining == 1 ? string.Empty : "s");
                    default:
                        return "This copy needs a licence key";
                }
            }
        }

        public string RequestBlurb
        {
            get
            {
                return "Licences are free for teaching use. This opens a pre-filled email for you to send - nothing is sent "
                    + "automatically. It includes how much the app has been used, shown in full below.";
            }
        }

        public string Name { get { return _name; } set { if (Set(ref _name, value ?? string.Empty)) Raise(nameof(Preview)); } }
        public string School { get { return _school; } set { if (Set(ref _school, value ?? string.Empty)) Raise(nameof(Preview)); } }
        public string Email { get { return _email; } set { if (Set(ref _email, value ?? string.Empty)) Raise(nameof(Preview)); } }
        public string Note { get { return _note; } set { if (Set(ref _note, value ?? string.Empty)) Raise(nameof(Preview)); } }
        public IReadOnlyList<string> RequestKindNames { get { return RequestKinds; } }
        public int RequestKind { get { return _requestKind; } set { if (Set(ref _requestKind, value)) Raise(nameof(Preview)); } }

        public ICommand SendRequest { get; }
        public ICommand CopyRequest { get; }
        public ICommand OpenWebPage { get; }

        public string Subject()
        {
            return "Gym Clock licence request - " + Licensing.MachineId();
        }

        public string Body()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Gym Interval Clock - licence request");
            sb.AppendLine();
            sb.AppendLine("Name         : " + _name.Trim());
            sb.AppendLine("School       : " + _school.Trim());
            sb.AppendLine("Email        : " + _email.Trim());
            sb.AppendLine("Request type : " + (_requestKind >= 0 && _requestKind < RequestKinds.Count ? RequestKinds[_requestKind] : string.Empty));
            sb.AppendLine();
            sb.AppendLine("Computer ID  : " + Licensing.MachineId());
            sb.AppendLine("App version  : " + System.Windows.Forms.Application.ProductVersion);

            if (_status != null && _status.Licence != null && !string.IsNullOrEmpty(_status.Licence.Name))
            {
                sb.AppendLine("Current key  : " + _status.Licence.Name);
                if (_status.Licence.Expires.HasValue)
                    sb.AppendLine("Expires      : " + _status.Licence.Expires.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            sb.AppendLine();
            sb.AppendLine(UsageLog.Summary());

            UsageSnapshot usage = UsageLog.Snapshot();
            UsageAssessment.Result assessment = UsageAssessment.Evaluate(usage);
            sb.AppendLine("Licence type : " + assessment.Headline);
            if (assessment.Reasons.Count > 0) sb.AppendLine("Because      : " + string.Join("; ", assessment.Reasons.ToArray()));
            foreach (string flag in assessment.Flags) sb.AppendLine("Flag         : " + flag);
            string code = UsageAssessment.Code(usage, assessment);
            if (code.Length > 0) sb.AppendLine("Usage code   : " + code);

            string note = _note.Trim();
            if (note.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Note         : " + note);
            }
            return sb.ToString();
        }

        /// <summary>"This is what will be sent".</summary>
        public string Preview
        {
            get
            {
                return "To: " + Licensing.ContactDetails + Environment.NewLine
                    + "Subject: " + Subject() + Environment.NewLine + Environment.NewLine + Body();
            }
        }

        /// <summary>Null when the request can be sent, otherwise what is missing.</summary>
        public string Problem()
        {
            if (_name.Trim().Length < 2) return "Please enter your name.";
            if (_school.Trim().Length < 2) return "Please enter your school or organisation.";
            if (!_email.Contains("@") || _email.Trim().Length < 5) return "Please enter an email address so a reply can be sent.";
            return null;
        }

        private void Remember()
        {
            RequesterDetails details = new RequesterDetails { Name = _name.Trim(), School = _school.Trim(), Email = _email.Trim() };
            details.Save();
        }

        private void DoSendRequest()
        {
            string problem = Problem();
            if (problem != null)
            {
                _dialogs.Info("Request a licence", problem);
                return;
            }

            Remember();
            Licensing.RecordRenewalPrompt(14);   // a fortnight before asking again

            string url = "mailto:" + Uri.EscapeDataString(Licensing.ContactDetails)
                + "?subject=" + Uri.EscapeDataString(Subject())
                + "&body=" + Uri.EscapeDataString(Body());

            if (!_dialogs.Open(url))
            {
                _dialogs.Info("Request a licence",
                    "No email program could be opened on this computer." + Environment.NewLine + Environment.NewLine
                    + "Use \"Copy details\" instead and paste them into an email, or Teams, or the web page.");
                return;
            }

            _dialogs.Info("Request a licence",
                "Your email program should now be open with the request filled in." + Environment.NewLine + Environment.NewLine
                + "Press Send there - it will not go until you do." + Environment.NewLine + Environment.NewLine
                + "Once you get a reply, come back here (F3 on the clock) and click \"Check online\".");
        }

        private void DoCopyRequest()
        {
            Remember();
            if (_dialogs.CopyText(Preview))
                _dialogs.Info("Request a licence", "Copied. Paste it into an email or a Teams message to " + Licensing.ContactDetails + ".");
            else
                _dialogs.Info("Request a licence", "The clipboard could not be used. Select the text in the box and copy it manually.");
        }
    }
}
