using System;
using System.Drawing;
using System.Windows.Forms;

namespace GymClock
{
    public partial class LicenceDialog
    {
        private System.ComponentModel.IContainer components = null;

        private Label _title;
        private Label _copyrightLabel;
        private Label _statusLabel;

        private Label _machineLabel;
        private TextBox _machineBox;
        private Button _copyMachineButton;

        private Label _keyLabel;
        private TextBox _keyBox;

        private Button _activateButton;
        private Button _fromFileButton;
        private Button _checkOnlineButton;
        private Button _closeButton;
        private Button _requestButton;
        private Button _saveReportButton;

        private Label _contactLabel;
        private LinkLabel _siteLink;
        private Label _followUpLabel;
        private LinkLabel _updateLink;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            _title = new Label();
            _copyrightLabel = new Label();
            _statusLabel = new Label();
            _machineLabel = new Label();
            _machineBox = new TextBox();
            _copyMachineButton = new Button();
            _keyLabel = new Label();
            _keyBox = new TextBox();
            _activateButton = new Button();
            _fromFileButton = new Button();
            _checkOnlineButton = new Button();
            _closeButton = new Button();
            _requestButton = new Button();
            _saveReportButton = new Button();
            _contactLabel = new Label();
            _siteLink = new LinkLabel();
            _followUpLabel = new Label();
            _updateLink = new LinkLabel();
            SuspendLayout();

            // _title
            _title.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            _title.Location = new Point(18, 16);
            _title.Name = "_title";
            _title.Size = new Size(500, 28);
            _title.Text = "Gym Interval Clock";

            // _copyrightLabel
            _copyrightLabel.ForeColor = Color.DimGray;
            _copyrightLabel.Location = new Point(18, 48);
            _copyrightLabel.Name = "_copyrightLabel";
            _copyrightLabel.Size = new Size(500, 20);
            _copyrightLabel.Text = "Copyright (c) 2026 Chris Bucknell and James Vella. All rights reserved.";

            // _statusLabel
            _statusLabel.Location = new Point(18, 78);
            _statusLabel.Name = "_statusLabel";
            _statusLabel.Size = new Size(500, 76);

            // _machineLabel
            _machineLabel.Location = new Point(18, 166);
            _machineLabel.Name = "_machineLabel";
            _machineLabel.Size = new Size(140, 22);
            _machineLabel.Text = "This computer's ID:";

            // _machineBox
            _machineBox.Font = new Font("Consolas", 11F, FontStyle.Bold);
            _machineBox.Location = new Point(164, 162);
            _machineBox.Name = "_machineBox";
            _machineBox.ReadOnly = true;
            _machineBox.Size = new Size(170, 26);
            _machineBox.TabStop = false;

            // _copyMachineButton
            _copyMachineButton.Location = new Point(342, 161);
            _copyMachineButton.Name = "_copyMachineButton";
            _copyMachineButton.Size = new Size(80, 28);
            _copyMachineButton.TabIndex = 0;
            _copyMachineButton.Text = "Copy";
            _copyMachineButton.UseVisualStyleBackColor = true;
            _copyMachineButton.Click += new EventHandler(CopyMachine_Click);

            // _keyLabel
            _keyLabel.Location = new Point(18, 200);
            _keyLabel.Name = "_keyLabel";
            _keyLabel.Size = new Size(500, 20);
            _keyLabel.Text = "Licence key:";

            // _keyBox
            _keyBox.Font = new Font("Consolas", 9.5F);
            _keyBox.Location = new Point(18, 222);
            _keyBox.Multiline = true;
            _keyBox.Name = "_keyBox";
            _keyBox.ScrollBars = ScrollBars.Vertical;
            _keyBox.Size = new Size(500, 76);
            _keyBox.TabIndex = 1;

            // _activateButton
            _activateButton.Location = new Point(18, 308);
            _activateButton.Name = "_activateButton";
            _activateButton.Size = new Size(110, 32);
            _activateButton.TabIndex = 2;
            _activateButton.Text = "Activate";
            _activateButton.UseVisualStyleBackColor = true;
            _activateButton.Click += new EventHandler(Activate_Click);

            // _fromFileButton
            _fromFileButton.Location = new Point(138, 308);
            _fromFileButton.Name = "_fromFileButton";
            _fromFileButton.Size = new Size(140, 32);
            _fromFileButton.TabIndex = 3;
            _fromFileButton.Text = "Load from file...";
            _fromFileButton.UseVisualStyleBackColor = true;
            _fromFileButton.Click += new EventHandler(FromFile_Click);

            // _checkOnlineButton
            _checkOnlineButton.Location = new Point(288, 308);
            _checkOnlineButton.Name = "_checkOnlineButton";
            _checkOnlineButton.Size = new Size(120, 32);
            _checkOnlineButton.TabIndex = 4;
            _checkOnlineButton.Text = "Check online";
            _checkOnlineButton.UseVisualStyleBackColor = true;
            _checkOnlineButton.Click += new EventHandler(CheckOnline_Click);

            // _closeButton
            _closeButton.DialogResult = DialogResult.OK;
            _closeButton.Location = new Point(428, 308);
            _closeButton.Name = "_closeButton";
            _closeButton.Size = new Size(90, 32);
            _closeButton.TabIndex = 5;
            _closeButton.Text = "Close";
            _closeButton.UseVisualStyleBackColor = true;

            // _requestButton
            _requestButton.Location = new Point(18, 348);
            _requestButton.Name = "_requestButton";
            _requestButton.Size = new Size(160, 32);
            _requestButton.TabIndex = 6;
            _requestButton.Text = "Request a licence";
            _requestButton.UseVisualStyleBackColor = true;
            _requestButton.Click += new EventHandler(Request_Click);

            // _saveReportButton
            _saveReportButton.Location = new Point(188, 348);
            _saveReportButton.Name = "_saveReportButton";
            _saveReportButton.Size = new Size(160, 32);
            _saveReportButton.TabIndex = 7;
            _saveReportButton.Text = "Save report...";
            _saveReportButton.UseVisualStyleBackColor = true;
            _saveReportButton.Click += new EventHandler(SaveReport_Click);

            // _contactLabel
            _contactLabel.Font = new Font("Segoe UI", 8.5F);
            _contactLabel.ForeColor = Color.DimGray;
            _contactLabel.Location = new Point(18, 392);
            _contactLabel.Name = "_contactLabel";
            _contactLabel.Size = new Size(500, 18);
            _contactLabel.Text = "To request a licence, copy the computer ID above and apply at:";

            // _siteLink
            _siteLink.Font = new Font("Segoe UI", 9F);
            _siteLink.Location = new Point(18, 412);
            _siteLink.Name = "_siteLink";
            _siteLink.Size = new Size(500, 20);
            _siteLink.TabStop = false;
            _siteLink.Text = "https://buckas.github.io/gymclock-licences";
            _siteLink.LinkClicked += new LinkLabelLinkClickedEventHandler(SiteLink_Clicked);

            // _followUpLabel
            _followUpLabel.Font = new Font("Segoe UI", 8.5F);
            _followUpLabel.ForeColor = Color.DimGray;
            _followUpLabel.Location = new Point(18, 436);
            _followUpLabel.Name = "_followUpLabel";
            _followUpLabel.Size = new Size(500, 34);
            _followUpLabel.Text = "Once approved, press \"Check online\" and the key will install itself.";

            // _updateLink
            _updateLink.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold);
            _updateLink.Location = new Point(18, 472);
            _updateLink.Name = "_updateLink";
            _updateLink.Size = new Size(500, 24);
            _updateLink.TabStop = false;
            _updateLink.Visible = false;
            _updateLink.LinkClicked += new LinkLabelLinkClickedEventHandler(UpdateLink_Clicked);

            // LicenceDialog
            AcceptButton = _activateButton;
            AutoScaleMode = AutoScaleMode.None;
            CancelButton = _closeButton;
            ClientSize = new Size(536, 508);
            Controls.Add(_title);
            Controls.Add(_copyrightLabel);
            Controls.Add(_statusLabel);
            Controls.Add(_machineLabel);
            Controls.Add(_machineBox);
            Controls.Add(_copyMachineButton);
            Controls.Add(_keyLabel);
            Controls.Add(_keyBox);
            Controls.Add(_activateButton);
            Controls.Add(_fromFileButton);
            Controls.Add(_checkOnlineButton);
            Controls.Add(_closeButton);
            Controls.Add(_requestButton);
            Controls.Add(_saveReportButton);
            Controls.Add(_contactLabel);
            Controls.Add(_siteLink);
            Controls.Add(_followUpLabel);
            Controls.Add(_updateLink);
            Font = new Font("Segoe UI", 10F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            KeyPreview = true;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "LicenceDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "About Gym Interval Clock";
            TopMost = true;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
    }
}
