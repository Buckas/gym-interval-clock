using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GymClock.AI;

namespace GymClock
{
    public sealed class ProgramAssistantForm : Form
    {
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

        private readonly RichTextBox rtbConversation = new RichTextBox();
        private readonly TextBox txtRequest = new TextBox();
        private readonly Button btnSend = new Button();
        private readonly Button btnCancel = new Button();
        private readonly Button btnNewConversation = new Button();
        private readonly ComboBox cboSuggestedPrompt = new ComboBox();
        private readonly RichTextBox rtbProposedScript = new RichTextBox();
        private readonly Label lblLocalAiStatus = new Label();
        private readonly Label lblValidationStatus = new Label();
        private readonly ListBox lstIssues = new ListBox();
        private readonly Label lblProgramSummary = new Label();
        private readonly Button btnValidate = new Button();
        private readonly Button btnApply = new Button();
        private readonly Button btnCopyScript = new Button();
        private readonly Label lblPrivacy = new Label();
        private readonly Panel pnlSetup = new Panel();
        private readonly Label lblSetup = new Label();
        private readonly Button btnSetup = new Button();
        private readonly Button btnRetry = new Button();
        private readonly Button btnDetails = new Button();

        private string _lastSetupDetails = string.Empty;
        private bool _localAiReady;

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

        private void InitializeUi()
        {
            Text = "Program Assistant (Local AI)";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1120, 760);
            MinimumSize = new Size(980, 680);
            Font = new Font("Segoe UI", 10F);
            ShowInTaskbar = false;
            TopMost = true;

            lblLocalAiStatus.Text = "Local AI status: FREE / ON DEVICE";
            lblLocalAiStatus.AutoSize = true;
            lblLocalAiStatus.Location = new Point(12, 10);

            pnlSetup.Location = new Point(12, 62);
            pnlSetup.Size = new Size(1078, 106);
            pnlSetup.BorderStyle = BorderStyle.FixedSingle;

            lblSetup.Location = new Point(10, 10);
            lblSetup.Size = new Size(1048, 52);
            lblSetup.Text = "Local AI runs on this computer and has no per-request API charge."
                + Environment.NewLine
                + "An internet connection may be required for the initial runtime/model download."
                + Environment.NewLine
                + "No cloud AI service will be used by Gym Interval Clock Version 3.0.";

            btnSetup.Text = "Set Up Local AI";
            btnSetup.Location = new Point(10, 66);
            btnSetup.Size = new Size(132, 30);
            btnSetup.Click += async (s, e) => await SetupLocalAiAsync();

            btnRetry.Text = "Retry";
            btnRetry.Location = new Point(148, 66);
            btnRetry.Size = new Size(90, 30);
            btnRetry.Click += async (s, e) => await SetupLocalAiAsync();

            btnDetails.Text = "View Details";
            btnDetails.Location = new Point(244, 66);
            btnDetails.Size = new Size(112, 30);
            btnDetails.Click += (s, e) => ShowSetupDetails();

            pnlSetup.Controls.Add(lblSetup);
            pnlSetup.Controls.Add(btnSetup);
            pnlSetup.Controls.Add(btnRetry);
            pnlSetup.Controls.Add(btnDetails);

            lblPrivacy.Text = "Do not include student names or confidential health information in AI requests.";
            lblPrivacy.AutoSize = true;
            lblPrivacy.ForeColor = Color.DarkGoldenrod;
            lblPrivacy.Location = new Point(12, 34);

            Label lblConversation = new Label { Text = "Conversation", AutoSize = true, Location = new Point(12, 176) };
            rtbConversation.Location = new Point(12, 200);
            rtbConversation.Size = new Size(530, 210);
            rtbConversation.ReadOnly = true;

            Label lblRequest = new Label { Text = "Request", AutoSize = true, Location = new Point(12, 418) };
            txtRequest.Location = new Point(12, 442);
            txtRequest.Multiline = true;
            txtRequest.Size = new Size(530, 110);

            cboSuggestedPrompt.Location = new Point(12, 560);
            cboSuggestedPrompt.Size = new Size(530, 28);
            cboSuggestedPrompt.DropDownStyle = ComboBoxStyle.DropDownList;
            cboSuggestedPrompt.Items.AddRange(new object[]
            {
                "Create a 6-station shared timing class with 40s work and 20s rest for 12 rounds.",
                "Create an 8-station rotating circuit with 45s work, 15s rest, and 20s move time.",
                "Revise this program to reduce total duration to around 30 minutes without changing mode."
            });
            cboSuggestedPrompt.SelectedIndexChanged += SuggestedPrompt_SelectedIndexChanged;

            btnSend.Text = "Generate / Revise";
            btnSend.Location = new Point(12, 596);
            btnSend.Size = new Size(165, 36);
            btnSend.Click += async (s, e) => await GenerateAsync();

            btnCancel.Text = "Cancel";
            btnCancel.Location = new Point(183, 596);
            btnCancel.Size = new Size(100, 36);
            btnCancel.Click += (s, e) => _requestCts?.Cancel();

            btnNewConversation.Text = "New Conversation";
            btnNewConversation.Location = new Point(289, 596);
            btnNewConversation.Size = new Size(150, 36);
            btnNewConversation.Click += (s, e) => NewConversation();

            Label lblProposal = new Label { Text = "Proposed Script", AutoSize = true, Location = new Point(560, 176) };
            rtbProposedScript.Location = new Point(560, 200);
            rtbProposedScript.Size = new Size(530, 350);
            rtbProposedScript.TextChanged += ProposedScript_TextChanged;

            lblValidationStatus.Text = "Validation: Not validated";
            lblValidationStatus.AutoSize = true;
            lblValidationStatus.Location = new Point(560, 558);

            btnValidate.Text = "Validate";
            btnValidate.Location = new Point(560, 584);
            btnValidate.Size = new Size(95, 36);
            btnValidate.Click += (s, e) => ValidateCurrentProposal();

            btnApply.Text = "Apply to Editor";
            btnApply.Location = new Point(661, 584);
            btnApply.Size = new Size(140, 36);
            btnApply.Click += (s, e) => ApplyToEditor();

            btnCopyScript.Text = "Copy Script";
            btnCopyScript.Location = new Point(807, 584);
            btnCopyScript.Size = new Size(110, 36);
            btnCopyScript.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(rtbProposedScript.Text)) Clipboard.SetText(rtbProposedScript.Text);
            };

            lblProgramSummary.Text = "Summary: -";
            lblProgramSummary.Location = new Point(560, 626);
            lblProgramSummary.Size = new Size(530, 60);

            Label lblIssues = new Label { Text = "Validation Issues", AutoSize = true, Location = new Point(12, 638) };
            lstIssues.Location = new Point(12, 662);
            lstIssues.Size = new Size(1078, 56);

            Controls.Add(lblLocalAiStatus);
            Controls.Add(lblPrivacy);
            Controls.Add(pnlSetup);
            Controls.Add(lblConversation);
            Controls.Add(rtbConversation);
            Controls.Add(lblRequest);
            Controls.Add(txtRequest);
            Controls.Add(cboSuggestedPrompt);
            Controls.Add(btnSend);
            Controls.Add(btnCancel);
            Controls.Add(btnNewConversation);
            Controls.Add(lblProposal);
            Controls.Add(rtbProposedScript);
            Controls.Add(lblValidationStatus);
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

        private void ApplySetupStatus(LocalAiSetupStatus status)
        {
            LocalAiSetupStatus s = status ?? new LocalAiSetupStatus
            {
                Stage = LocalAiSetupStage.NotStarted,
                Message = "Local AI not set up"
            };

            _lastSetupDetails = s.Details ?? string.Empty;

            LocalAiSetupUiState uiState = LocalAiSetupUiStateEvaluator.Evaluate(s, _isBusy, _options.DeveloperDiagnostics);
            _localAiReady = uiState.LocalAiReady;

            lblLocalAiStatus.Text = uiState.StatusLine;
            lblSetup.Text = uiState.SetupMessage;
            btnSetup.Enabled = uiState.EnableSetup;
            btnRetry.Enabled = uiState.EnableRetry;
            btnDetails.Enabled = uiState.EnableDetails;
            btnSend.Enabled = !_isBusy && _localAiReady;
        }

        private void ShowSetupDetails()
        {
            string text = string.IsNullOrWhiteSpace(_lastSetupDetails)
                ? "No additional details are available."
                : _lastSetupDetails;

            MessageBox.Show(this, text, "Local AI Details", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task GenerateAsync()
        {
            if (_isBusy) return;
            if (!_localAiReady)
            {
                MessageBox.Show(this,
                    "Local AI is not ready yet. Click 'Set Up Local AI' first.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string request = txtRequest.Text.Trim();
            if (request.Length == 0)
            {
                MessageBox.Show(this, "Enter a request first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SetBusy(true);
            _requestCts?.Dispose();
            _requestCts = new CancellationTokenSource();
            CancellationTokenSource timeoutCts = new CancellationTokenSource(_options.GenerationTimeout);
            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_requestCts.Token, timeoutCts.Token);

            try
            {
                AiProgramProposal proposal = await _coordinator.GenerateAsync(
                    request,
                    _baselineScript,
                    _conversation,
                    linked.Token).ConfigureAwait(true);

                if (_isClosing || IsDisposed) return;

                _session.SetGeneratedProposal(proposal);
                SetProposalText(proposal.Script);
                RenderValidation(proposal.Issues, proposal.ParsedProgram, proposal.IsValid ? ProposalValidationState.Valid : ProposalValidationState.Invalid);
                AppendConversation("User", request);
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
                SetBusy(false);
            }
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
            RenderValidation(Array.Empty<ProgramValidationIssue>(), null, ProposalValidationState.NotValidated);
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
            ResetConversationUi();
        }

        private void ResetConversationUi()
        {
            rtbConversation.Clear();
            SetProposalText(string.Empty);
            RenderValidation(Array.Empty<ProgramValidationIssue>(), null, ProposalValidationState.NotValidated);
            txtRequest.Clear();
        }

        private void SetProposalText(string text)
        {
            _updatingProposalText = true;
            try
            {
                rtbProposedScript.Text = text ?? string.Empty;
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
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            btnSend.Enabled = !busy && _localAiReady;
            btnCancel.Enabled = busy;
            btnApply.Enabled = !busy && _session.CanApply;
            btnNewConversation.Enabled = !busy;
            btnValidate.Enabled = !busy;
            cboSuggestedPrompt.Enabled = !busy;
            txtRequest.Enabled = !busy;
            rtbProposedScript.ReadOnly = busy;
            btnSetup.Enabled = !busy && !_localAiReady;
            btnRetry.Enabled = !busy && !_localAiReady;
            btnDetails.Enabled = !busy;

            lblLocalAiStatus.Text = busy
                ? "Local AI status: generating... FREE / ON DEVICE"
                : "Local AI status: ready - FREE / ON DEVICE";
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
