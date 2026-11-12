using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GymClock.AI;

namespace GymClock
{
    public sealed class ProgramAssistantForm : Form
    {
        private enum ClarificationStage
        {
            None,
            AwaitRevisionStationPolicy,
            AwaitStationReplacement,
            AwaitMode,
            AwaitStructure,
            AwaitDurationTolerance,
            AwaitConfirmation
        }

        private readonly AiProgramGenerationCoordinator _coordinator;
        private readonly ProgramProposalValidator _validator;
        private readonly AiAssistantOptions _options;
        private readonly ILocalAiSetupService _setupService;
        private readonly ConversationState _conversation = new ConversationState();
        private readonly ProgramAssistantSessionState _session = new ProgramAssistantSessionState();
        private readonly string _baselineScript;

        private CancellationTokenSource _requestCts;
        private bool _isBusy;
        private bool _updatingProposalText;
        private bool _isClosing;
        private bool _isHighlightingIssueLines;

        private readonly RichTextBox rtbConversation = new RichTextBox();
        private readonly TextBox txtRequest = new TextBox();
        private readonly Button btnSend = new Button();
        private readonly Button btnCancel = new Button();
        private readonly Button btnNewConversation = new Button();
        private readonly Button btnLoadCurrentProgram = new Button();
        private readonly ComboBox cboSuggestedPrompt = new ComboBox();
        private readonly RichTextBox rtbProposedScript = new RichTextBox();
        private readonly Label lblLocalAiStatus = new Label();
        private readonly Label lblValidationStatus = new Label();
        private readonly Label lblConversationMore = new Label();
        private readonly ListBox lstIssues = new ListBox();
        private readonly Label lblProgramSummary = new Label();
        private readonly Button btnValidate = new Button();
        private readonly Button btnApply = new Button();
        private readonly Button btnCopyScript = new Button();
        private readonly Label lblPrivacy = new Label();
        private readonly Panel pnlSetup = new Panel();
        private readonly Label lblSetup = new Label();
        private readonly ProgressBar prgSetup = new ProgressBar();
        private readonly Label lblSetupProgress = new Label();
        private readonly Button btnSetup = new Button();
        private readonly Button btnRetry = new Button();
        private readonly Button btnDetails = new Button();

        private string _lastSetupDetails = string.Empty;
        private bool _localAiReady;
        private bool _autoSetupStarted;
        private bool _isGenerating;
        private bool _awaitingModeClarification;
        private ClarificationStage _clarificationStage;
        private string _pendingClarificationRequest = string.Empty;
        private string _pendingClarificationMode = string.Empty;
        private string _pendingStructureInstruction = string.Empty;
        private string _pendingStructureSummary = string.Empty;
        private string _pendingDurationInstruction = string.Empty;
        private string _pendingDurationSummary = string.Empty;
        private string _pendingRevisionStationPolicyInstruction = string.Empty;
        private string _pendingRevisionStationPolicySummary = string.Empty;
        private string _pendingReplacementTarget = string.Empty;
        private string _pendingReplacementOption1 = string.Empty;
        private string _pendingReplacementOption2 = string.Empty;
        private string _pendingReplacementOption3 = string.Empty;
        private List<ProgramValidationIssue> _currentIssues = new List<ProgramValidationIssue>();
        private readonly List<int> _highlightedIssueLines = new List<int>();

        private const int EmGetFirstVisibleLine = 0x00CE;

        public string AppliedScript { get; private set; }

        public ProgramAssistantForm(
            AiProgramGenerationCoordinator coordinator,
            ProgramProposalValidator validator,
            AiAssistantOptions options,
            ILocalAiSetupService setupService,
            string baselineScript)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _setupService = setupService;
            _baselineScript = baselineScript ?? string.Empty;

            InitializeUi();
            ResetConversationUi();
            SetBusy(false);
            Shown += ProgramAssistantForm_Shown;

            if (_setupService != null)
            {
                ApplySetupStatus(_setupService.LastSetupStatus ?? new LocalAiSetupStatus
                {
                    Stage = LocalAiSetupStage.NotStarted,
                    Message = "Local AI not set up"
                });
            }
            else
            {
                ApplySetupStatus(new LocalAiSetupStatus
                {
                    Stage = LocalAiSetupStage.Unavailable,
                    Message = "Local AI unavailable",
                    Details = "No local setup service is configured."
                });
            }
        }

        private static string NormalizeCommonAiAliases(string script)
        {
            string text = script ?? string.Empty;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("LABEL ", StringComparison.OrdinalIgnoreCase))
                {
                    int indentLength = line.Length - trimmed.Length;
                    string indent = indentLength > 0 ? line.Substring(0, indentLength) : string.Empty;
                    lines[i] = indent + "WORKNOTE " + trimmed.Substring(6).TrimStart();
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private void InitializeUi()
        {
            Text = "Program Assistant (Local AI)";
            Icon = AppIconProvider.GetIcon();
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1120, 860);
            MinimumSize = new Size(980, 780);
            Font = new Font("Segoe UI", 10F);
            ShowInTaskbar = false;
            TopMost = true;

            lblLocalAiStatus.Text = "Local AI status: FREE / ON DEVICE";
            lblLocalAiStatus.AutoSize = true;
            lblLocalAiStatus.Location = new Point(12, 10);

            pnlSetup.Location = new Point(12, 62);
            pnlSetup.Size = new Size(1078, 144);
            pnlSetup.BorderStyle = BorderStyle.FixedSingle;

            lblSetup.Location = new Point(10, 10);
            lblSetup.Size = new Size(1048, 74);
            lblSetup.Text = "Local AI runs on this computer and has no per-request API charge."
                + Environment.NewLine
                + "An internet connection may be required for the initial runtime/model download."
                + Environment.NewLine
                + "No cloud AI service will be used by Gym Interval Clock Version 3.0.";

            btnSetup.Text = "Set Up Local AI";
            btnSetup.Location = new Point(10, 82);
            btnSetup.Size = new Size(132, 30);
            btnSetup.Click += async (s, e) => await SetupLocalAiAsync();

            btnRetry.Text = "Retry";
            btnRetry.Location = new Point(148, 82);
            btnRetry.Size = new Size(90, 30);
            btnRetry.Click += async (s, e) => await SetupLocalAiAsync();

            btnDetails.Text = "View Details";
            btnDetails.Location = new Point(244, 82);
            btnDetails.Size = new Size(112, 30);
            btnDetails.Click += (s, e) => ShowSetupDetails();

            prgSetup.Location = new Point(10, 126);
            prgSetup.Size = new Size(1048, 12);
            prgSetup.Style = ProgressBarStyle.Marquee;
            prgSetup.MarqueeAnimationSpeed = 25;
            prgSetup.Visible = false;

            lblSetupProgress.AutoSize = true;
            lblSetupProgress.Location = new Point(10, 102);
            lblSetupProgress.ForeColor = Color.DimGray;
            lblSetupProgress.Text = string.Empty;
            lblSetupProgress.Visible = false;

            pnlSetup.Controls.Add(lblSetup);
            pnlSetup.Controls.Add(prgSetup);
            pnlSetup.Controls.Add(lblSetupProgress);
            pnlSetup.Controls.Add(btnSetup);
            pnlSetup.Controls.Add(btnRetry);
            pnlSetup.Controls.Add(btnDetails);

            lblPrivacy.Text = "Do not include student names or confidential health information in AI requests.";
            lblPrivacy.AutoSize = true;
            lblPrivacy.ForeColor = Color.DarkGoldenrod;
            lblPrivacy.Location = new Point(12, 34);

            Label lblConversation = new Label { Text = "Conversation", AutoSize = true, Location = new Point(12, 208) };
            rtbConversation.Location = new Point(12, 232);
            rtbConversation.Size = new Size(530, 210);
            rtbConversation.ReadOnly = true;
            rtbConversation.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtbConversation.VScroll += (s, e) => UpdateConversationOverflowIndicator();
            rtbConversation.Resize += (s, e) => UpdateConversationOverflowIndicator();
            rtbConversation.MouseWheel += (s, e) => UpdateConversationOverflowIndicator();

            lblConversationMore.AutoSize = true;
            lblConversationMore.ForeColor = Color.DimGray;
            lblConversationMore.BackColor = Color.WhiteSmoke;
            lblConversationMore.Text = "▼ More below";
            lblConversationMore.Location = new Point(200, 236);
            lblConversationMore.Visible = false;

            Label lblRequest = new Label { Text = "Request", AutoSize = true, Location = new Point(12, 450) };
            txtRequest.Location = new Point(12, 474);
            txtRequest.Multiline = true;
            txtRequest.Size = new Size(530, 110);

            cboSuggestedPrompt.Location = new Point(12, 592);
            cboSuggestedPrompt.Size = new Size(530, 28);
            cboSuggestedPrompt.DropDownStyle = ComboBoxStyle.DropDownList;
            cboSuggestedPrompt.Items.AddRange(new object[]
            {
                "Create a MODE SHARED whole-group HIIT with 40s work / 20s rest for 12 rounds.",
                "Create a MODE SEQUENTIAL station circuit with 6 stations and BETWEEN MOVE 20s.",
                "Create a MODE PARALLEL program with mixed station durations (sprint + endurance lanes).",
                "Create a MODE CIRCUIT rotating group class with shared REPEAT timing and BETWEEN transitions.",
                "Revise the current program to reduce total duration to about 30 minutes without changing mode.",
                "Keep this mode and station list, but increase beginner-friendliness with easier timings and coaching notes."
            });
            cboSuggestedPrompt.SelectedIndexChanged += SuggestedPrompt_SelectedIndexChanged;

            btnSend.Text = "✨ Generate / Revise";
            btnSend.Location = new Point(12, 628);
            btnSend.Size = new Size(210, 42);
            btnSend.Click += async (s, e) => await GenerateAsync();

            btnCancel.Text = "Cancel";
            btnCancel.Location = new Point(228, 628);
            btnCancel.Size = new Size(88, 42);
            btnCancel.Click += (s, e) => _requestCts?.Cancel();

            btnNewConversation.Text = "New Chat";
            btnNewConversation.Location = new Point(322, 628);
            btnNewConversation.Size = new Size(100, 42);
            btnNewConversation.Click += (s, e) => NewConversation();

            btnLoadCurrentProgram.Text = "Load Program";
            btnLoadCurrentProgram.Location = new Point(428, 628);
            btnLoadCurrentProgram.Size = new Size(114, 42);
            btnLoadCurrentProgram.Click += (s, e) => LoadCurrentProgramIntoAssistant();

            Label lblProposal = new Label { Text = "Proposed Script", AutoSize = true, Location = new Point(560, 208) };
            rtbProposedScript.Location = new Point(560, 232);
            rtbProposedScript.Size = new Size(530, 350);
            rtbProposedScript.TextChanged += ProposedScript_TextChanged;

            lblValidationStatus.Text = "Validation: Not validated";
            lblValidationStatus.AutoSize = true;
            lblValidationStatus.Location = new Point(560, 590);

            btnValidate.Text = "Validate";
            btnValidate.Location = new Point(560, 624);
            btnValidate.Size = new Size(95, 36);
            btnValidate.Click += (s, e) => ValidateCurrentProposal();

            Label lblValidationHelp = new Label
            {
                Text = "Validation runs automatically. Use Validate only after manual script edits.",
                AutoSize = false,
                ForeColor = Color.DimGray,
                Location = new Point(560, 666),
                Size = new Size(530, 34)
            };

            btnApply.Text = "Apply to Editor";
            btnApply.Location = new Point(661, 624);
            btnApply.Size = new Size(170, 36);
            btnApply.Click += (s, e) => ApplyToEditor();

            btnCopyScript.Text = "Copy Script";
            btnCopyScript.Location = new Point(837, 624);
            btnCopyScript.Size = new Size(120, 36);
            btnCopyScript.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(rtbProposedScript.Text)) Clipboard.SetText(rtbProposedScript.Text);
            };

            lblProgramSummary.Text = "Summary: -";
            lblProgramSummary.Location = new Point(560, 708);
            lblProgramSummary.Size = new Size(530, 24);

            Label lblIssues = new Label { Text = "Validation Issues", AutoSize = true, Location = new Point(12, 728) };
            lstIssues.Location = new Point(12, 750);
            lstIssues.Size = new Size(1078, 56);
            lstIssues.SelectedIndexChanged += Issues_SelectedIndexChanged;

            Controls.Add(lblLocalAiStatus);
            Controls.Add(lblPrivacy);
            Controls.Add(pnlSetup);
            Controls.Add(lblConversation);
            Controls.Add(lblConversationMore);
            Controls.Add(rtbConversation);
            Controls.Add(lblRequest);
            Controls.Add(txtRequest);
            Controls.Add(cboSuggestedPrompt);
            Controls.Add(btnSend);
            Controls.Add(btnCancel);
            Controls.Add(btnNewConversation);
            Controls.Add(btnLoadCurrentProgram);
            Controls.Add(lblProposal);
            Controls.Add(rtbProposedScript);
            Controls.Add(lblValidationStatus);
            Controls.Add(lblValidationHelp);
            Controls.Add(btnValidate);
            Controls.Add(btnApply);
            Controls.Add(btnCopyScript);
            Controls.Add(lblProgramSummary);
            Controls.Add(lblIssues);
            Controls.Add(lstIssues);

            FormClosing += ProgramAssistantForm_FormClosing;
        }

        private async Task SetupLocalAiAsync()
        {
            if (_setupService == null)
            {
                ApplySetupStatus(new LocalAiSetupStatus
                {
                    Stage = LocalAiSetupStage.Unavailable,
                    Message = "Local AI unavailable",
                    Details = "No local setup service is configured."
                });
                return;
            }

            if (_isBusy) return;

            SetBusy(true);
            _requestCts?.Dispose();
            _requestCts = new CancellationTokenSource();

            try
            {
                LocalAiSetupStatus status = await _setupService
                    .EnsureReadyAsync(ApplySetupStatus, _requestCts.Token)
                    .ConfigureAwait(true);
                ApplySetupStatus(status);
            }
            catch (OperationCanceledException)
            {
                ApplySetupStatus(new LocalAiSetupStatus
                {
                    Stage = LocalAiSetupStage.NotStarted,
                    Message = "Local AI setup cancelled"
                });
            }
            catch (LocalAiSetupException ex)
            {
                ApplySetupStatus(new LocalAiSetupStatus
                {
                    Stage = LocalAiSetupStage.Failed,
                    Message = "Local AI could not be started.",
                    Details = ex.InnerException == null ? ex.ToString() : ex.InnerException.ToString()
                });
            }
            catch (Exception ex)
            {
                ApplySetupStatus(new LocalAiSetupStatus
                {
                    Stage = LocalAiSetupStage.Failed,
                    Message = "Local AI could not be started.",
                    Details = ex.ToString()
                });
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void ProgramAssistantForm_Shown(object sender, EventArgs e)
        {
            if (_autoSetupStarted || _isClosing) return;
            _autoSetupStarted = true;

            if (_setupService == null || _localAiReady) return;

            await SetupLocalAiAsync();
        }

        private void ApplySetupStatus(LocalAiSetupStatus status)
        {
            if (_isClosing || IsDisposed) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<LocalAiSetupStatus>(ApplySetupStatus), status);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            LocalAiSetupStatus s = status ?? new LocalAiSetupStatus
            {
                Stage = LocalAiSetupStage.NotStarted,
                Message = "Local AI not set up"
            };

            _lastSetupDetails = s.Details ?? string.Empty;

            LocalAiSetupUiState uiState = LocalAiSetupUiStateEvaluator.Evaluate(s, _isBusy, _options.DeveloperDiagnostics);
            _localAiReady = uiState.LocalAiReady;
            bool showManualSetupActions = s.IsFailure;

            lblLocalAiStatus.Text = uiState.StatusLine;
            lblSetup.Text = uiState.SetupMessage;

            btnSetup.Visible = showManualSetupActions;
            btnRetry.Visible = showManualSetupActions;
            btnDetails.Visible = showManualSetupActions;

            btnSetup.Enabled = showManualSetupActions && uiState.EnableSetup;
            btnRetry.Enabled = showManualSetupActions && uiState.EnableRetry;
            btnDetails.Enabled = showManualSetupActions && uiState.EnableDetails;
            btnSend.Enabled = !_isBusy && _localAiReady;

            bool setupInProgress = !_localAiReady && !s.IsFailure
                && (s.Stage == LocalAiSetupStage.CheckingCompatibility
                    || s.Stage == LocalAiSetupStage.DownloadingComponents
                    || s.Stage == LocalAiSetupStage.DownloadingModel
                    || s.Stage == LocalAiSetupStage.LoadingModel);

            prgSetup.Visible = setupInProgress;
            lblSetupProgress.Visible = setupInProgress;
            if (setupInProgress && s.ProgressPercent.HasValue)
            {
                prgSetup.Style = ProgressBarStyle.Continuous;
                int value = (int)Math.Max(0, Math.Min(100, s.ProgressPercent.Value));
                prgSetup.Value = value;
                lblSetupProgress.Text = "Progress: " + value + "%";
            }
            else if (setupInProgress)
            {
                prgSetup.Style = ProgressBarStyle.Marquee;
                lblSetupProgress.Text = "Progress: working...";
            }

            if (_isGenerating)
            {
                prgSetup.Visible = true;
                lblSetupProgress.Visible = true;
                prgSetup.Style = ProgressBarStyle.Marquee;
                lblSetupProgress.Text = "Progress: generating...";
                lblLocalAiStatus.Text = "Local AI status: generating program draft...";
                lblSetup.Text = "Generating a proposal. This can take a little while on first use."
                    + Environment.NewLine
                    + "You can press Cancel to stop and keep your current script unchanged.";
                Cursor = Cursors.AppStarting;
            }
            else if (setupInProgress)
            {
                lblSetup.Text = uiState.SetupMessage + Environment.NewLine
                    + "You can type now. Generate enables automatically once Local AI is ready.";
                Cursor = Cursors.AppStarting;
            }
            else
            {
                lblSetup.Text = uiState.SetupMessage;
                lblSetupProgress.Text = string.Empty;
                lblSetupProgress.Visible = false;
                Cursor = Cursors.Default;
            }
        }

        private void ShowSetupDetails()
        {
            string text = string.IsNullOrWhiteSpace(_lastSetupDetails)
                ? "No additional details are available."
                : _lastSetupDetails;

            using (Form details = new Form())
            {
                details.Text = "Local AI Details";
                details.StartPosition = FormStartPosition.CenterParent;
                details.Size = new Size(980, 620);
                details.MinimumSize = new Size(760, 460);
                details.Font = Font;

                TextBox txt = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    WordWrap = false,
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 10F),
                    Text = text
                };

                Panel bottom = new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 48
                };

                Button btnCopy = new Button
                {
                    Text = "Copy",
                    Size = new Size(90, 30),
                    Location = new Point(10, 9)
                };
                btnCopy.Click += (s, e) => Clipboard.SetText(txt.Text ?? string.Empty);

                Button btnClose = new Button
                {
                    Text = "Close",
                    Size = new Size(90, 30),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };
                btnClose.Location = new Point(bottom.Width - 100, 9);
                bottom.Resize += (s, e) => btnClose.Location = new Point(bottom.Width - 100, 9);
                btnClose.Click += (s, e) => details.Close();

                bottom.Controls.Add(btnCopy);
                bottom.Controls.Add(btnClose);
                details.Controls.Add(txt);
                details.Controls.Add(bottom);

                details.ShowDialog(this);
            }
        }

        private async Task GenerateAsync()
        {
            if (_isBusy) return;
            if (!_localAiReady)
            {
                MessageBox.Show(this,
                    "Local AI is not ready yet. Wait for setup to complete, or use Retry if setup failed.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string userInput = txtRequest.Text.Trim();
            if (userInput.Length == 0)
            {
                MessageBox.Show(this, "Enter a request first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            txtRequest.Clear();

            if (TryHandleClarificationInput(userInput, out string clarifiedRequest, out string assistantReply, out bool shouldGenerate))
            {
                AppendConversation("User", userInput);
                if (!string.IsNullOrWhiteSpace(assistantReply))
                {
                    AppendConversation("Assistant", assistantReply);
                }

                if (!shouldGenerate)
                {
                    return;
                }

                userInput = clarifiedRequest;
            }

            string request = userInput;

            _isGenerating = true;
            SetBusy(true);
            _requestCts?.Dispose();
            _requestCts = new CancellationTokenSource();
            CancellationTokenSource timeoutCts = new CancellationTokenSource(_options.GenerationTimeout);
            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_requestCts.Token, timeoutCts.Token);

            try
            {
                AiProgramProposal proposal = await _coordinator.GenerateAsync(
                    request,
                    GetContextScriptForGeneration(),
                    _conversation,
                    linked.Token).ConfigureAwait(true);

                AppendConversation("User", userInput);

                if (proposal.RequiresClarification)
                {
                    _awaitingModeClarification = true;
                    _pendingClarificationRequest = userInput;
                    _pendingClarificationMode = string.Empty;
                    _pendingStructureInstruction = string.Empty;
                    _pendingStructureSummary = string.Empty;
                    _pendingDurationInstruction = string.Empty;
                    _pendingDurationSummary = string.Empty;
                    _pendingRevisionStationPolicyInstruction = string.Empty;
                    _pendingRevisionStationPolicySummary = string.Empty;
                    _pendingReplacementTarget = string.Empty;
                    _pendingReplacementOption1 = string.Empty;
                    _pendingReplacementOption2 = string.Empty;
                    _pendingReplacementOption3 = string.Empty;

                    string clarificationText = proposal.AssistantMessage?.Trim() ?? string.Empty;
                    if (clarificationText.IndexOf("station-change policy", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _clarificationStage = ClarificationStage.AwaitRevisionStationPolicy;
                        AppendConversation("Assistant", clarificationText);
                    }
                    else if (TryInitializeStationReplacementClarificationState(clarificationText))
                    {
                        _clarificationStage = ClarificationStage.AwaitStationReplacement;
                        AppendConversation("Assistant", clarificationText);
                    }
                    else
                    {
                        _clarificationStage = ClarificationStage.AwaitMode;
                        AppendConversation("Assistant", BuildModeQuestion(proposal.AssistantMessage));
                    }
                    return;
                }

                _awaitingModeClarification = false;
                _clarificationStage = ClarificationStage.None;
                _pendingClarificationRequest = string.Empty;
                _pendingClarificationMode = string.Empty;
                _pendingStructureInstruction = string.Empty;
                _pendingStructureSummary = string.Empty;
                _pendingDurationInstruction = string.Empty;
                _pendingDurationSummary = string.Empty;

                if (!proposal.IsValid)
                {
                    AiProgramProposal repaired = await TryAutoRepairAsync(proposal.Script, linked.Token).ConfigureAwait(true);
                    if (repaired != null)
                    {
                        proposal = repaired;
                    }
                }

                if (_isClosing || IsDisposed) return;

                _session.SetGeneratedProposal(proposal);
                SetProposalText(proposal.Script);
                RenderValidation(proposal.Issues, proposal.ParsedProgram, proposal.IsValid ? ProposalValidationState.Valid : ProposalValidationState.Invalid);
                AppendConversation("Assistant", proposal.IsValid
                    ? "Generated a valid proposal."
                    : "Generated a proposal with validation issues.");
            }
            catch (OperationCanceledException)
            {
                if (!_isClosing && !IsDisposed)
                {
                    AppendConversation("Assistant", "Generation cancelled.");
                }
            }
            catch (Exception ex)
            {
                if (!_isClosing && !IsDisposed)
                {
                    MessageBox.Show(this, "Generation failed:" + Environment.NewLine + ex.Message,
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                linked.Dispose();
                timeoutCts.Dispose();
                _isGenerating = false;
                SetBusy(false);
            }
        }

        private async Task<AiProgramProposal> TryAutoRepairAsync(string invalidScript, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(invalidScript)) return null;

            try
            {
                AiProgramProposal repaired = await _coordinator.GenerateAsync(
                    "Fix all validation errors in the current draft. Keep the same workout intent and mode. Return only corrected script.",
                    invalidScript,
                    _conversation,
                    cancellationToken).ConfigureAwait(true);

                if (repaired.IsValid)
                {
                    AppendConversation("Assistant", "Auto-repaired validation issues.");
                    return repaired;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }

            return null;
        }

        private void ValidateCurrentProposal()
        {
            ProgramValidationResult validation = _validator.Validate(rtbProposedScript.Text);
            _session.SetValidationResult(validation);
            RenderValidation(validation.Issues, validation.ParsedProgram,
                validation.IsValid ? ProposalValidationState.Valid : ProposalValidationState.Invalid);
        }

        private void ApplyToEditor()
        {
            if (!_session.CanApply)
            {
                MessageBox.Show(this,
                    "The proposal is not currently valid/applicable. Validate it again before applying.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            AppliedScript = _session.CurrentProposalScript;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ProposedScript_TextChanged(object sender, EventArgs e)
        {
            if (_updatingProposalText) return;

            _session.SetProposalTextEditedByUser(rtbProposedScript.Text);
            ProgramValidationResult validation = _validator.Validate(rtbProposedScript.Text);
            _session.SetValidationResult(validation);
            RenderValidation(validation.Issues, validation.ParsedProgram,
                validation.IsValid ? ProposalValidationState.Valid : ProposalValidationState.Invalid);
        }

        private void SuggestedPrompt_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cboSuggestedPrompt.SelectedItem == null) return;
            txtRequest.Text = cboSuggestedPrompt.SelectedItem.ToString();
            txtRequest.SelectionStart = txtRequest.TextLength;
            txtRequest.SelectionLength = 0;
            txtRequest.Focus();
        }

        private void NewConversation()
        {
            _conversation.Clear();
            _session.Clear();
            ResetClarificationState();
            ResetConversationUi();
        }

        private void LoadCurrentProgramIntoAssistant()
        {
            SetProposalText(_baselineScript);
            ProgramValidationResult validation = _validator.Validate(_baselineScript);
            _session.SetValidationResult(validation);
            RenderValidation(validation.Issues, validation.ParsedProgram,
                validation.IsValid ? ProposalValidationState.Valid : ProposalValidationState.Invalid);
            AppendConversation("Assistant", "Loaded current program into the draft editor.");
        }

        private string GetContextScriptForGeneration()
        {
            string draft = rtbProposedScript.Text ?? string.Empty;
            ProgramValidationResult draftValidation = _validator.Validate(draft);
            if (draftValidation.IsValid)
            {
                return draftValidation.Script;
            }

            return _baselineScript;
        }

        private void ResetConversationUi()
        {
            rtbConversation.Clear();
            UpdateConversationOverflowIndicator();
            SetProposalText(string.Empty);
            RenderValidation(Array.Empty<ProgramValidationIssue>(), null, ProposalValidationState.NotValidated);
            txtRequest.Clear();
        }

        private void SetProposalText(string text)
        {
            _updatingProposalText = true;
            try
            {
                rtbProposedScript.Text = NormalizeCommonAiAliases(text);
            }
            finally
            {
                _updatingProposalText = false;
            }
        }

        private void RenderValidation(
            IReadOnlyList<ProgramValidationIssue> issues,
            WorkoutProgram parsed,
            ProposalValidationState state)
        {
            lstIssues.Items.Clear();
            foreach (ProgramValidationIssue issue in issues ?? Array.Empty<ProgramValidationIssue>())
            {
                string linePrefix = issue.LineNumber.HasValue ? "Line " + issue.LineNumber.Value + ": " : string.Empty;
                lstIssues.Items.Add(issue.Severity + " - " + linePrefix + issue.Message);
            }

            _currentIssues = (issues ?? Array.Empty<ProgramValidationIssue>()).ToList();
            HighlightIssueLines(null);

            switch (state)
            {
                case ProposalValidationState.Valid:
                    lblValidationStatus.Text = "Validation: Valid";
                    lblValidationStatus.ForeColor = Color.DarkGreen;
                    break;
                case ProposalValidationState.Invalid:
                    lblValidationStatus.Text = "Validation: Invalid";
                    lblValidationStatus.ForeColor = Color.DarkRed;
                    break;
                default:
                    lblValidationStatus.Text = "Validation: Not validated";
                    lblValidationStatus.ForeColor = Color.DimGray;
                    break;
            }

            lblProgramSummary.Text = parsed == null
                ? "Summary: -"
                : BuildSummary(parsed);

            btnApply.Enabled = _session.CanApply && !_isBusy;
        }

        private void HighlightIssueLines(int? emphasizedLine)
        {
            if (_isHighlightingIssueLines) return;
            _isHighlightingIssueLines = true;

            int originalStart = rtbProposedScript.SelectionStart;
            int originalLength = rtbProposedScript.SelectionLength;

            try
            {
                foreach (int line in _highlightedIssueLines.ToList())
                {
                    if (line < 0 || line >= rtbProposedScript.Lines.Length) continue;
                    int start = rtbProposedScript.GetFirstCharIndexFromLine(line);
                    if (start < 0) continue;

                    string lineText = rtbProposedScript.Lines[line] ?? string.Empty;
                    rtbProposedScript.SelectionStart = start;
                    rtbProposedScript.SelectionLength = Math.Max(1, lineText.Length);
                    rtbProposedScript.SelectionBackColor = rtbProposedScript.BackColor;
                }

                _highlightedIssueLines.Clear();

                HashSet<int> issueLines = new HashSet<int>();
                foreach (ProgramValidationIssue issue in _currentIssues)
                {
                    if (!issue.LineNumber.HasValue || issue.LineNumber.Value <= 0) continue;
                    int line = issue.LineNumber.Value - 1;
                    if (line < 0 || line >= rtbProposedScript.Lines.Length) continue;
                    issueLines.Add(line);
                }

                foreach (int line in issueLines.OrderBy(x => x))
                {
                    int start = rtbProposedScript.GetFirstCharIndexFromLine(line);
                    if (start < 0) continue;

                    string lineText = rtbProposedScript.Lines[line] ?? string.Empty;
                    rtbProposedScript.SelectionStart = start;
                    rtbProposedScript.SelectionLength = Math.Max(1, lineText.Length);
                    rtbProposedScript.SelectionBackColor = line == emphasizedLine
                        ? Color.FromArgb(255, 230, 180)
                        : Color.FromArgb(255, 244, 214);
                    _highlightedIssueLines.Add(line);
                }

                int maxSelection = rtbProposedScript.TextLength;
                rtbProposedScript.SelectionStart = Math.Min(Math.Max(0, originalStart), maxSelection);
                rtbProposedScript.SelectionLength = Math.Max(0, Math.Min(originalLength, Math.Max(0, maxSelection - rtbProposedScript.SelectionStart)));
                rtbProposedScript.SelectionBackColor = rtbProposedScript.BackColor;
            }
            finally
            {
                _isHighlightingIssueLines = false;
            }
        }

        private static string BuildSummary(WorkoutProgram program)
        {
            string finite = "Yes";
            int seconds;

            if (program.Mode == ExecutionMode.Parallel)
            {
                List<List<ResolvedBlock>> lanes = program.BuildParallelSequences();
                int max = 0;
                foreach (List<ResolvedBlock> lane in lanes)
                {
                    int laneSeconds = lane.Sum(x => Math.Max(0, x.Block.Seconds));
                    if (laneSeconds > max) max = laneSeconds;
                }
                seconds = max;
            }
            else
            {
                seconds = program.BuildRuntimeSequence().Sum(x => Math.Max(0, x.Block.Seconds));
            }

            string duration = IntervalPlan.FormatDuration(seconds);
            return "Mode: " + program.Mode
                + " | Stations: " + program.Stations.Count
                + " | Estimated duration: " + duration
                + " | Finite program: " + finite;
        }

        private void AppendConversation(string role, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (rtbConversation.TextLength > 0) rtbConversation.AppendText(Environment.NewLine + Environment.NewLine);
            rtbConversation.AppendText(role + ": " + text.Trim());
            rtbConversation.SelectionStart = rtbConversation.TextLength;
            rtbConversation.SelectionLength = 0;
            rtbConversation.ScrollToCaret();
            UpdateConversationOverflowIndicator();
        }

        private void UpdateConversationOverflowIndicator()
        {
            if (rtbConversation.IsDisposed) return;

            int firstVisibleLine = GetFirstVisibleLine(rtbConversation);
            int totalLines = Math.Max(0, rtbConversation.GetLineFromCharIndex(rtbConversation.TextLength) + 1);
            int visibleLines = Math.Max(1, rtbConversation.ClientSize.Height / Math.Max(1, rtbConversation.Font.Height));
            bool hasMoreBelow = totalLines > visibleLines && firstVisibleLine + visibleLines < totalLines;

            int indicatorX = rtbConversation.Left + Math.Max(4, (rtbConversation.Width - lblConversationMore.Width) / 2);
            int indicatorY = rtbConversation.Top + 6;
            lblConversationMore.Location = new Point(indicatorX, indicatorY);

            lblConversationMore.Visible = hasMoreBelow;
            lblConversationMore.BringToFront();
        }

        private static int GetFirstVisibleLine(RichTextBox box)
        {
            if (box == null || box.IsDisposed) return 0;
            return (int)SendMessage(box.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            bool lockInputsForGeneration = busy && _isGenerating;
            btnSend.Text = busy && _isGenerating ? "✨ Generating..." : "✨ Generate / Revise";
            btnCancel.Enabled = busy;
            btnApply.Enabled = !busy && _session.CanApply;
            btnNewConversation.Enabled = !lockInputsForGeneration;
            btnLoadCurrentProgram.Enabled = !lockInputsForGeneration;
            btnValidate.Enabled = !lockInputsForGeneration;
            cboSuggestedPrompt.Enabled = !lockInputsForGeneration;
            txtRequest.Enabled = !lockInputsForGeneration;
            rtbProposedScript.ReadOnly = lockInputsForGeneration;

            LocalAiSetupStatus status = _setupService?.LastSetupStatus;
            if (status == null)
            {
                status = new LocalAiSetupStatus
                {
                    Stage = _localAiReady ? LocalAiSetupStage.Ready : LocalAiSetupStage.NotStarted,
                    Message = _localAiReady ? "Local AI ready - FREE / ON DEVICE" : "Local AI not set up"
                };
            }

            ApplySetupStatus(status);
        }

        private bool TryHandleClarificationInput(string userInput, out string requestToGenerate, out string assistantReply, out bool shouldGenerate)
        {
            requestToGenerate = userInput ?? string.Empty;
            assistantReply = string.Empty;
            shouldGenerate = true;

            string text = (userInput ?? string.Empty).Trim();
            if (text.Length == 0) return false;

            if (_clarificationStage == ClarificationStage.None && !_awaitingModeClarification)
            {
                return false;
            }

            shouldGenerate = false;

            if (_clarificationStage == ClarificationStage.None)
            {
                _clarificationStage = ClarificationStage.AwaitMode;
            }

            switch (_clarificationStage)
            {
                case ClarificationStage.AwaitMode:
                    if (!TryGetModeFromChoice(text, out string mode))
                    {
                        assistantReply = "I still need the mode choice. Reply with 1, 2, 3, or 4." + Environment.NewLine
                            + "Quick replies: 1=SHARED, 2=SEQUENTIAL, 3=PARALLEL, 4=CIRCUIT";
                        return true;
                    }

                    _pendingClarificationMode = mode;
                    _clarificationStage = ClarificationStage.AwaitStructure;
                    assistantReply = BuildStructureQuestion(mode);
                    return true;

                case ClarificationStage.AwaitRevisionStationPolicy:
                    if (!TryGetRevisionStationPolicyChoice(text, out string stationPolicyInstruction, out string stationPolicySummary))
                    {
                        assistantReply = BuildRevisionStationPolicyQuestion();
                        return true;
                    }

                    _pendingRevisionStationPolicyInstruction = stationPolicyInstruction;
                    _pendingRevisionStationPolicySummary = stationPolicySummary;
                    shouldGenerate = true;
                    requestToGenerate = BuildRevisionClarifiedRequest();
                    assistantReply = "Generating now with station policy: " + stationPolicySummary;
                    ResetClarificationState();
                    return true;

                case ClarificationStage.AwaitStationReplacement:
                    if (!TryGetStationReplacementChoice(
                        text,
                        _pendingReplacementOption1,
                        _pendingReplacementOption2,
                        _pendingReplacementOption3,
                        out string replacement,
                        out string replacementPrompt))
                    {
                        assistantReply = replacementPrompt;
                        return true;
                    }

                    shouldGenerate = true;
                    requestToGenerate = BuildStationReplacementClarifiedRequest(replacement);
                    assistantReply = "Generating now with replacement: " + replacement;
                    ResetClarificationState();
                    return true;

                case ClarificationStage.AwaitStructure:
                    if (!TryGetStructureChoice(_pendingClarificationMode, text, out string structureInstruction, out string structureSummary))
                    {
                        assistantReply = BuildStructureQuestion(_pendingClarificationMode);
                        return true;
                    }

                    _pendingStructureInstruction = structureInstruction;
                    _pendingStructureSummary = structureSummary;
                    _clarificationStage = ClarificationStage.AwaitDurationTolerance;
                    assistantReply = BuildDurationToleranceQuestion();
                    return true;

                case ClarificationStage.AwaitDurationTolerance:
                    if (!TryGetDurationToleranceChoice(text, out string durationInstruction, out string durationSummary))
                    {
                        assistantReply = BuildDurationToleranceQuestion();
                        return true;
                    }

                    _pendingDurationInstruction = durationInstruction;
                    _pendingDurationSummary = durationSummary;
                    _clarificationStage = ClarificationStage.AwaitConfirmation;
                    assistantReply = BuildConfirmationPrompt();
                    return true;

                case ClarificationStage.AwaitConfirmation:
                    if (IsYes(text))
                    {
                        shouldGenerate = true;
                        requestToGenerate = BuildClarifiedGenerationRequest();
                        assistantReply = "Generating now with: " + _pendingClarificationMode
                            + " | " + _pendingStructureSummary
                            + " | " + _pendingDurationSummary;
                        ResetClarificationState();
                        return true;
                    }

                    if (IsNo(text))
                    {
                        _clarificationStage = ClarificationStage.AwaitMode;
                        assistantReply = BuildModeQuestion(null);
                        return true;
                    }

                    if (string.Equals(text, "edit", StringComparison.OrdinalIgnoreCase))
                    {
                        _clarificationStage = ClarificationStage.AwaitStructure;
                        assistantReply = BuildStructureQuestion(_pendingClarificationMode);
                        return true;
                    }

                    assistantReply = BuildConfirmationPrompt();
                    return true;
            }

            return true;
        }

        private string BuildModeQuestion(string fallback)
        {
            string baseText = string.IsNullOrWhiteSpace(fallback)
                ? "Before I generate, choose the mode so the script validates correctly:" + Environment.NewLine
                    + "1) MODE SHARED - one timeline for the whole group." + Environment.NewLine
                    + "2) MODE SEQUENTIAL - station-by-station rotation (each station needs timing blocks)." + Environment.NewLine
                    + "3) MODE PARALLEL - station lanes run at the same time." + Environment.NewLine
                    + "4) MODE CIRCUIT - shared rounds repeated across a station list." + Environment.NewLine
                    + "Reply with a number (1-4) or type the mode name."
                : fallback.Trim();

            if (baseText.IndexOf("Quick replies:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return baseText;
            }

            return baseText + Environment.NewLine + "Quick replies: 1, 2, 3, 4";
        }

        private string BuildRevisionStationPolicyQuestion()
        {
            return "To revise duration safely, choose station-change policy:" + Environment.NewLine
                + "1) Keep station count and station details unchanged." + Environment.NewLine
                + "2) Keep station count, allow station detail updates." + Environment.NewLine
                + "3) Allow station count and detail changes if needed." + Environment.NewLine
                + "Reply with 1, 2, or 3." + Environment.NewLine
                + "Quick replies: 1, 2, 3";
        }

        private string BuildStructureQuestion(string mode)
        {
            if (string.Equals(mode, "SEQUENTIAL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "PARALLEL", StringComparison.OrdinalIgnoreCase))
            {
                return "Great. Choose station structure:" + Environment.NewLine
                    + "1) 6 stations, 3 rounds" + Environment.NewLine
                    + "2) 8 stations, 3 rounds" + Environment.NewLine
                    + "3) 10 stations, 2 rounds" + Environment.NewLine
                    + "4) Auto choose structure" + Environment.NewLine
                    + "Quick replies: 1, 2, 3, 4";
            }

            return "Great. Choose station structure:" + Environment.NewLine
                + "1) 6 stations, 5 rounds" + Environment.NewLine
                + "2) 8 stations, 4 rounds" + Environment.NewLine
                + "3) 10 stations, 3 rounds" + Environment.NewLine
                + "4) Auto choose structure" + Environment.NewLine
                + "Quick replies: 1, 2, 3, 4";
        }

        private static bool TryGetStructureChoice(string mode, string input, out string instruction, out string summary)
        {
            instruction = string.Empty;
            summary = string.Empty;

            if (!TryGetNumericChoice(input, out int choice) || choice < 1 || choice > 4) return false;

            bool laneMode = string.Equals(mode, "SEQUENTIAL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "PARALLEL", StringComparison.OrdinalIgnoreCase);

            if (choice == 4)
            {
                if (laneMode)
                {
                    instruction = "Use at least 6 stations and include both cardio and resistance exercises; choose rounds to fit requested duration.";
                    summary = "Auto structure (min 6 stations)";
                }
                else
                {
                    instruction = "Use at least 8 stations with a cardio/resistance mix (at least 4 cardio and 4 resistance); choose rounds to fit requested duration.";
                    summary = "Auto structure (min 8 stations)";
                }
                return true;
            }

            if (laneMode)
            {
                if (choice == 1) { instruction = "Use 6 stations and 3 rounds."; summary = "6 stations / 3 rounds"; return true; }
                if (choice == 2) { instruction = "Use 8 stations and 3 rounds."; summary = "8 stations / 3 rounds"; return true; }
                instruction = "Use 10 stations and 2 rounds."; summary = "10 stations / 2 rounds"; return true;
            }

            if (choice == 1) { instruction = "Use 6 stations and 5 rounds."; summary = "6 stations / 5 rounds"; return true; }
            if (choice == 2) { instruction = "Use 8 stations and 4 rounds."; summary = "8 stations / 4 rounds"; return true; }
            instruction = "Use 10 stations and 3 rounds."; summary = "10 stations / 3 rounds"; return true;
        }

        private static string BuildDurationToleranceQuestion()
        {
            return "Choose duration strictness:" + Environment.NewLine
                + "1) Strict (target as close as possible)" + Environment.NewLine
                + "2) Balanced (within about ±5 minutes)" + Environment.NewLine
                + "3) Flexible (within about ±8 minutes)" + Environment.NewLine
                + "Quick replies: 1, 2, 3";
        }

        private static bool TryGetDurationToleranceChoice(string input, out string instruction, out string summary)
        {
            instruction = string.Empty;
            summary = string.Empty;

            if (TryGetNumericChoice(input, out int choice))
            {
                if (choice == 1)
                {
                    instruction = "Keep total duration very close to the requested time.";
                    summary = "Strict";
                    return true;
                }

                if (choice == 2)
                {
                    instruction = "Keep total duration within about plus or minus 5 minutes from the requested time.";
                    summary = "Balanced (±5m)";
                    return true;
                }

                if (choice == 3)
                {
                    instruction = "Keep total duration within about plus or minus 8 minutes from the requested time.";
                    summary = "Flexible (±8m)";
                    return true;
                }
            }

            string text = (input ?? string.Empty).Trim();
            if (text.IndexOf("strict", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                instruction = "Keep total duration very close to the requested time.";
                summary = "Strict";
                return true;
            }

            if (text.IndexOf("balanced", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("5", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                instruction = "Keep total duration within about plus or minus 5 minutes from the requested time.";
                summary = "Balanced (±5m)";
                return true;
            }

            if (text.IndexOf("flexible", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("8", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                instruction = "Keep total duration within about plus or minus 8 minutes from the requested time.";
                summary = "Flexible (±8m)";
                return true;
            }

            return false;
        }

        private string BuildConfirmationPrompt()
        {
            return "Confirm generation settings:" + Environment.NewLine
                + "- Mode: " + _pendingClarificationMode + Environment.NewLine
                + "- Structure: " + _pendingStructureSummary + Environment.NewLine
                + "- Duration: " + _pendingDurationSummary + Environment.NewLine
                + "Reply Y to generate, EDIT to change structure, or N to restart mode selection." + Environment.NewLine
                + "Quick replies: Y, EDIT, N";
        }

        private string BuildClarifiedGenerationRequest()
        {
            string context = string.IsNullOrWhiteSpace(_pendingClarificationRequest)
                ? "Create a program"
                : _pendingClarificationRequest.Trim();

            return context + Environment.NewLine
                + "Use MODE " + _pendingClarificationMode + "." + Environment.NewLine
                + _pendingStructureInstruction + Environment.NewLine
                + _pendingDurationInstruction + Environment.NewLine
                + "Include explicit cardio and resistance exercises in station names and/or notes.";
        }

        private string BuildRevisionClarifiedRequest()
        {
            string context = string.IsNullOrWhiteSpace(_pendingClarificationRequest)
                ? txtRequest.Text.Trim()
                : _pendingClarificationRequest.Trim();

            List<string> lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(context)) lines.Add(context);
            if (!string.IsNullOrWhiteSpace(_pendingRevisionStationPolicyInstruction)) lines.Add(_pendingRevisionStationPolicyInstruction);

            return string.Join(Environment.NewLine, lines);
        }

        private string BuildStationReplacementClarifiedRequest(string replacement)
        {
            string context = string.IsNullOrWhiteSpace(_pendingClarificationRequest)
                ? txtRequest.Text.Trim()
                : _pendingClarificationRequest.Trim();

            List<string> lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(context)) lines.Add(context);

            if (!string.IsNullOrWhiteSpace(_pendingReplacementTarget) && !string.IsNullOrWhiteSpace(replacement))
            {
                lines.Add("Replace station \"" + _pendingReplacementTarget + "\" with \"" + replacement.Trim() + "\".");
                lines.Add("Keep the same mode, station count, station order, timings, and all other station details unless explicitly changed above.");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private bool TryInitializeStationReplacementClarificationState(string assistantMessage)
        {
            string text = assistantMessage ?? string.Empty;
            if (text.IndexOf("choose a replacement for", StringComparison.OrdinalIgnoreCase) < 0) return false;

            Match target = Regex.Match(text, "choose a replacement for \\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase);
            if (!target.Success) return false;

            Match o1 = Regex.Match(text, @"^\s*1\)\s*(.+)$", RegexOptions.Multiline);
            Match o2 = Regex.Match(text, @"^\s*2\)\s*(.+)$", RegexOptions.Multiline);
            Match o3 = Regex.Match(text, @"^\s*3\)\s*(.+)$", RegexOptions.Multiline);
            if (!o1.Success || !o2.Success || !o3.Success) return false;

            _pendingReplacementTarget = target.Groups[1].Value.Trim();
            _pendingReplacementOption1 = o1.Groups[1].Value.Trim();
            _pendingReplacementOption2 = o2.Groups[1].Value.Trim();
            _pendingReplacementOption3 = o3.Groups[1].Value.Trim();
            return _pendingReplacementTarget.Length > 0;
        }

        private string BuildStationReplacementQuestion()
        {
            return "Before I revise, choose a replacement for \"" + _pendingReplacementTarget + "\":" + Environment.NewLine
                + "1) " + _pendingReplacementOption1 + Environment.NewLine
                + "2) " + _pendingReplacementOption2 + Environment.NewLine
                + "3) " + _pendingReplacementOption3 + Environment.NewLine
                + "4) Custom replacement (type the exact exercise name)" + Environment.NewLine
                + "Reply with 1-4, or type a replacement exercise name." + Environment.NewLine
                + "Quick replies: 1, 2, 3, 4";
        }

        private bool TryGetStationReplacementChoice(
            string input,
            string option1,
            string option2,
            string option3,
            out string replacement,
            out string prompt)
        {
            replacement = string.Empty;
            prompt = BuildStationReplacementQuestion();

            string text = (input ?? string.Empty).Trim();
            if (text.Length == 0) return false;

            if (TryGetNumericChoice(text, out int choice))
            {
                if (choice == 1) { replacement = option1; return true; }
                if (choice == 2) { replacement = option2; return true; }
                if (choice == 3) { replacement = option3; return true; }

                if (choice == 4)
                {
                    string tail = Regex.Replace(text, @"^\s*4\s*([\.:\-\)])?\s*", string.Empty).Trim();
                    if (tail.Length == 0)
                    {
                        prompt = "Type the custom replacement exercise name after 4 (for example: 4. Lat Dips).";
                        return false;
                    }

                    replacement = tail;
                    return true;
                }
            }

            replacement = text;
            return true;
        }

        private static bool TryGetRevisionStationPolicyChoice(string input, out string instruction, out string summary)
        {
            instruction = string.Empty;
            summary = string.Empty;

            string value = (input ?? string.Empty).Trim();
            if (value.Length == 0) return false;

            string normalized = value.ToLowerInvariant();
            if (normalized == "1" || normalized.Contains("unchanged") || normalized.Contains("keep station count and station details"))
            {
                instruction = "Preserve station count and station details from the current program. Do not rename, add, remove, or replace stations/exercises.";
                summary = "Keep station count and details unchanged";
                return true;
            }

            if (normalized == "2" || normalized.Contains("allow station detail") || normalized.Contains("keep station count"))
            {
                instruction = "Preserve station count from the current program. Station names/details may be updated only if needed.";
                summary = "Keep station count, allow detail updates";
                return true;
            }

            if (normalized == "3" || normalized.Contains("allow station count") || normalized.Contains("count and detail changes"))
            {
                instruction = "Station count and station details may change if required to satisfy the request.";
                summary = "Allow station count and detail changes";
                return true;
            }

            return false;
        }

        private static bool TryGetNumericChoice(string input, out int choice)
        {
            choice = 0;
            string text = (input ?? string.Empty).Trim();
            if (int.TryParse(text, out choice)) return true;

            string[] parts = text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { ' ', '\t', '\n', ',', ';', ':' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (int.TryParse(parts[i], out choice)) return true;
            }

            return false;
        }

        private static bool IsYes(string text)
        {
            return string.Equals(text, "y", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "generate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "go", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "ok", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNo(string text)
        {
            return string.Equals(text, "n", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "restart", StringComparison.OrdinalIgnoreCase);
        }

        private void ResetClarificationState()
        {
            _awaitingModeClarification = false;
            _clarificationStage = ClarificationStage.None;
            _pendingClarificationRequest = string.Empty;
            _pendingClarificationMode = string.Empty;
            _pendingStructureInstruction = string.Empty;
            _pendingStructureSummary = string.Empty;
            _pendingDurationInstruction = string.Empty;
            _pendingDurationSummary = string.Empty;
            _pendingRevisionStationPolicyInstruction = string.Empty;
            _pendingRevisionStationPolicySummary = string.Empty;
            _pendingReplacementTarget = string.Empty;
            _pendingReplacementOption1 = string.Empty;
            _pendingReplacementOption2 = string.Empty;
            _pendingReplacementOption3 = string.Empty;
        }

        private static bool TryGetModeFromChoice(string input, out string mode)
        {
            mode = string.Empty;
            string value = (input ?? string.Empty).Trim();
            if (value.Length == 0) return false;

            if (value.IndexOf("MODE SHARED", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mode = "SHARED";
                return true;
            }

            if (value.IndexOf("MODE SEQUENTIAL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mode = "SEQUENTIAL";
                return true;
            }

            if (value.IndexOf("MODE PARALLEL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mode = "PARALLEL";
                return true;
            }

            if (value.IndexOf("MODE CIRCUIT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mode = "CIRCUIT";
                return true;
            }

            string[] lineParts = value
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n', ' ', '\t', ',', ';', ':' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = lineParts.Length - 1; i >= 0; i--)
            {
                if (TryGetModeFromSingleToken(lineParts[i], out mode)) return true;
            }

            return TryGetModeFromSingleToken(value, out mode);
        }

        private static bool TryGetModeFromSingleToken(string token, out string mode)
        {
            mode = string.Empty;
            string value = (token ?? string.Empty).Trim();
            if (value.Length == 0) return false;

            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "shared", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "mode shared", StringComparison.OrdinalIgnoreCase))
            {
                mode = "SHARED";
                return true;
            }

            if (string.Equals(value, "2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "sequential", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "mode sequential", StringComparison.OrdinalIgnoreCase))
            {
                mode = "SEQUENTIAL";
                return true;
            }

            if (string.Equals(value, "3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "parallel", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "mode parallel", StringComparison.OrdinalIgnoreCase))
            {
                mode = "PARALLEL";
                return true;
            }

            if (string.Equals(value, "4", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "circuit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "mode circuit", StringComparison.OrdinalIgnoreCase))
            {
                mode = "CIRCUIT";
                return true;
            }

            return false;
        }

        private void Issues_SelectedIndexChanged(object sender, EventArgs e)
        {
            int index = lstIssues.SelectedIndex;
            if (index < 0 || index >= _currentIssues.Count) return;

            ProgramValidationIssue issue = _currentIssues[index];
            if (!issue.LineNumber.HasValue || issue.LineNumber.Value <= 0) return;

            int line = issue.LineNumber.Value - 1;
            if (line < 0 || line >= rtbProposedScript.Lines.Length) return;

            HighlightIssueLines(line);

            int start = rtbProposedScript.GetFirstCharIndexFromLine(line);
            if (start < 0) return;

            string lineText = rtbProposedScript.Lines[line] ?? string.Empty;
            rtbProposedScript.Focus();
            rtbProposedScript.SelectionStart = start;
            rtbProposedScript.SelectionLength = Math.Max(1, lineText.Length);
            rtbProposedScript.ScrollToCaret();
        }

        private void ProgramAssistantForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _isClosing = true;
            try
            {
                _requestCts?.Cancel();
            }
            catch
            {
            }

            _requestCts?.Dispose();
            _requestCts = null;
        }
    }
}
