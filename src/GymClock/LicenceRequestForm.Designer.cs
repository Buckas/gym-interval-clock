using System;
using System.Drawing;
using System.Windows.Forms;

namespace GymClock
{
    public partial class LicenceRequestForm
    {
        private System.ComponentModel.IContainer components = null;

        private Label _heading;
        private Label _blurb;

        private Label _nameLabel;
        private TextBox _name;

        private Label _schoolLabel;
        private TextBox _school;

        private Label _emailLabel;
        private TextBox _email;

        private Label _whichLabel;
        private ComboBox _which;

        private Label _noteLabel;
        private TextBox _note;

        private Label _previewLabel;
        private TextBox _preview;

        private Button _sendButton;
        private Button _copyButton;
        private Button _webButton;
        private Button _closeButton;

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
            _heading = new Label();
            _blurb = new Label();
            _nameLabel = new Label();
            _name = new TextBox();
            _schoolLabel = new Label();
            _school = new TextBox();
            _emailLabel = new Label();
            _email = new TextBox();
            _whichLabel = new Label();
            _which = new ComboBox();
            _noteLabel = new Label();
            _note = new TextBox();
            _previewLabel = new Label();
            _preview = new TextBox();
            _sendButton = new Button();
            _copyButton = new Button();
            _webButton = new Button();
            _closeButton = new Button();
            SuspendLayout();

            // _heading
            _heading.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            _heading.Location = new Point(18, 16);
            _heading.Name = "_heading";
            _heading.Size = new Size(520, 44);
            _heading.Text = "This copy needs a licence key";

            // _blurb
            _blurb.ForeColor = Color.DimGray;
            _blurb.Location = new Point(18, 66);
            _blurb.Name = "_blurb";
            _blurb.Size = new Size(520, 52);
            _blurb.Text = "Licences are free for teaching use. This opens a pre-filled email for " +
                "you to send - nothing is sent automatically. It includes how much the app has been " +
                "used, shown in full below.";

            // _nameLabel
            _nameLabel.Location = new Point(18, 130);
            _nameLabel.Name = "_nameLabel";
            _nameLabel.Size = new Size(124, 22);
            _nameLabel.Text = "Your name";

            // _name
            _name.Location = new Point(150, 126);
            _name.Name = "_name";
            _name.Size = new Size(388, 26);
            _name.TabIndex = 0;

            // _schoolLabel
            _schoolLabel.Location = new Point(18, 166);
            _schoolLabel.Name = "_schoolLabel";
            _schoolLabel.Size = new Size(124, 22);
            _schoolLabel.Text = "School or organisation";

            // _school
            _school.Location = new Point(150, 162);
            _school.Name = "_school";
            _school.Size = new Size(388, 26);
            _school.TabIndex = 1;

            // _emailLabel
            _emailLabel.Location = new Point(18, 202);
            _emailLabel.Name = "_emailLabel";
            _emailLabel.Size = new Size(124, 22);
            _emailLabel.Text = "Your email";

            // _email
            _email.Location = new Point(150, 198);
            _email.Name = "_email";
            _email.Size = new Size(388, 26);
            _email.TabIndex = 2;

            // _whichLabel
            _whichLabel.Location = new Point(18, 238);
            _whichLabel.Name = "_whichLabel";
            _whichLabel.Size = new Size(124, 22);
            _whichLabel.Text = "This computer is";

            // _which
            _which.DropDownStyle = ComboBoxStyle.DropDownList;
            _which.Items.AddRange(new object[] {
                "my first machine",
                "my second machine",
                "a replacement - please move a seat",
                "a renewal of an existing licence"});
            _which.Location = new Point(150, 234);
            _which.Name = "_which";
            _which.Size = new Size(260, 26);
            _which.TabIndex = 3;

            // _noteLabel
            _noteLabel.Location = new Point(18, 274);
            _noteLabel.Name = "_noteLabel";
            _noteLabel.Size = new Size(124, 22);
            _noteLabel.Text = "Anything else";

            // _note
            _note.Location = new Point(150, 270);
            _note.Name = "_note";
            _note.Size = new Size(388, 26);
            _note.TabIndex = 4;

            // _previewLabel
            _previewLabel.Location = new Point(18, 308);
            _previewLabel.Name = "_previewLabel";
            _previewLabel.Size = new Size(520, 20);
            _previewLabel.Text = "This is what will be sent:";

            // _preview
            _preview.BackColor = Color.FromArgb(246, 246, 244);
            _preview.Font = new Font("Consolas", 9F);
            _preview.Location = new Point(18, 330);
            _preview.Multiline = true;
            _preview.Name = "_preview";
            _preview.ReadOnly = true;
            _preview.ScrollBars = ScrollBars.Vertical;
            _preview.Size = new Size(520, 132);
            _preview.TabIndex = 5;
            _preview.TabStop = false;

            // _sendButton
            _sendButton.Location = new Point(18, 472);
            _sendButton.Name = "_sendButton";
            _sendButton.Size = new Size(130, 32);
            _sendButton.TabIndex = 6;
            _sendButton.Text = "Open in email";
            _sendButton.UseVisualStyleBackColor = true;
            _sendButton.Click += new EventHandler(Send_Click);

            // _copyButton
            _copyButton.Location = new Point(158, 472);
            _copyButton.Name = "_copyButton";
            _copyButton.Size = new Size(130, 32);
            _copyButton.TabIndex = 7;
            _copyButton.Text = "Copy details";
            _copyButton.UseVisualStyleBackColor = true;
            _copyButton.Click += new EventHandler(Copy_Click);

            // _webButton
            _webButton.Location = new Point(298, 472);
            _webButton.Name = "_webButton";
            _webButton.Size = new Size(130, 32);
            _webButton.TabIndex = 8;
            _webButton.Text = "Open web page";
            _webButton.UseVisualStyleBackColor = true;
            _webButton.Click += new EventHandler(Web_Click);

            // _closeButton
            _closeButton.DialogResult = DialogResult.Cancel;
            _closeButton.Location = new Point(448, 472);
            _closeButton.Name = "_closeButton";
            _closeButton.Size = new Size(90, 32);
            _closeButton.TabIndex = 9;
            _closeButton.Text = "Close";
            _closeButton.UseVisualStyleBackColor = true;

            // LicenceRequestForm
            AcceptButton = _sendButton;
            AutoScaleMode = AutoScaleMode.None;
            CancelButton = _closeButton;
            ClientSize = new Size(556, 516);
            Controls.Add(_heading);
            Controls.Add(_blurb);
            Controls.Add(_nameLabel);
            Controls.Add(_name);
            Controls.Add(_schoolLabel);
            Controls.Add(_school);
            Controls.Add(_emailLabel);
            Controls.Add(_email);
            Controls.Add(_whichLabel);
            Controls.Add(_which);
            Controls.Add(_noteLabel);
            Controls.Add(_note);
            Controls.Add(_previewLabel);
            Controls.Add(_preview);
            Controls.Add(_sendButton);
            Controls.Add(_copyButton);
            Controls.Add(_webButton);
            Controls.Add(_closeButton);
            Font = new Font("Segoe UI", 10F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            KeyPreview = true;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "LicenceRequestForm";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Request a licence";
            TopMost = true;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
    }
}
