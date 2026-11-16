using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace GymClock.Link
{
    /// <summary>
    /// The built-in Link displays guide (see <see cref="LinkHelp"/>): topics on the left, the page
    /// on the right, and - on the Bluetooth page - buttons that open the two Windows screens the
    /// steps use. Sized from the font, so it scales with Windows text size.
    /// </summary>
    public sealed class LinkHelpForm : Form
    {
        private readonly ListBox _topics = new ListBox();
        private readonly RichTextBox _page = new RichTextBox();
        private readonly FlowLayoutPanel _windowsButtons = new FlowLayoutPanel();
        private readonly List<Form> _wasTopMost = new List<Form>();

        public LinkHelpForm(string topicId)
        {
            Text = "Link displays - help";
            Icon = AppIconProvider.GetIcon();
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            TopMost = true; // the clock window is normally always-on-top
            int unit = Font.Height;
            ClientSize = new Size(unit * 46, unit * 30);
            MinimumSize = new Size(unit * 32, unit * 20);
            Padding = new Padding(10);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, unit * 11));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _topics.Dock = DockStyle.Fill;
            _topics.IntegralHeight = false;
            _topics.ItemHeight = unit + 8;
            _topics.DrawMode = DrawMode.OwnerDrawFixed;
            _topics.DrawItem += Topics_DrawItem;
            foreach (LinkHelpTopic topic in LinkHelp.Topics) _topics.Items.Add(topic.Title);
            _topics.SelectedIndexChanged += delegate { ShowTopic(); };
            root.Controls.Add(_topics, 0, 0);

            _page.Dock = DockStyle.Fill;
            _page.ReadOnly = true;
            _page.BorderStyle = BorderStyle.None;
            _page.BackColor = SystemColors.Window;
            _page.DetectUrls = false;
            _page.TabStop = false;
            _page.Margin = new Padding(8, 3, 3, 3);
            root.Controls.Add(_page, 1, 0);

            FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0) };
            Button close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(unit * 5, 0), DialogResult = DialogResult.OK };
            bottom.Controls.Add(close);

            // Only shown on the Bluetooth page.
            _windowsButtons.AutoSize = true;
            _windowsButtons.WrapContents = false;
            Button settings = new Button { Text = "Open Windows Bluetooth settings", AutoSize = true };
            settings.Click += delegate { OpenWindowsScreen("ms-settings:bluetooth", null); };
            Button pan = new Button { Text = "Open Personal Area Network devices", AutoSize = true };
            // The same window as the Bluetooth tray icon's "Join a Personal Area Network".
            pan.Click += delegate { OpenWindowsScreen("explorer.exe", "shell:::{28803F59-3A75-4058-995F-4EE5503B023C}"); };
            _windowsButtons.Controls.Add(settings);
            _windowsButtons.Controls.Add(pan);
            bottom.Controls.Add(_windowsButtons);

            root.Controls.Add(bottom, 0, 1);
            root.SetColumnSpan(bottom, 2);
            Controls.Add(root);
            AcceptButton = close;
            CancelButton = close;

            int index = 0;
            for (int i = 0; i < LinkHelp.Topics.Count; i++) if (LinkHelp.Topics[i].Id == topicId) index = i;
            _topics.SelectedIndex = index;
        }

        private void Topics_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            bool selected = (e.State & DrawItemState.Selected) != 0;
            TextRenderer.DrawText(e.Graphics, (string)_topics.Items[e.Index], Font,
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                selected ? SystemColors.HighlightText : SystemColors.ControlText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }

        private void ShowTopic()
        {
            if (_topics.SelectedIndex < 0) return;
            LinkHelpTopic topic = LinkHelp.Topics[_topics.SelectedIndex];
            _windowsButtons.Visible = topic.Id == LinkHelp.Bluetooth;

            _page.Clear();
            using (Font title = new Font(Font.FontFamily, Font.Size + 5f, FontStyle.Bold))
            using (Font heading = new Font(Font, FontStyle.Bold))
            {
                Append(topic.Title + Environment.NewLine, title, 0, 0, 6);
                foreach (string line in topic.Lines)
                {
                    if (line.StartsWith("# ", StringComparison.Ordinal))
                    {
                        Append(line.Substring(2) + Environment.NewLine, heading, 0, 0, 10);
                    }
                    else if (line.StartsWith("- ", StringComparison.Ordinal))
                    {
                        Append("•  " + line.Substring(2) + Environment.NewLine, Font, 14, 18, 2);
                    }
                    else if (line.Length > 2 && char.IsDigit(line[0]) && line[1] == '.')
                    {
                        Append(line.Substring(0, 2) + "  " + line.Substring(2).TrimStart() + Environment.NewLine, Font, 14, 22, 3);
                    }
                    else
                    {
                        Append(line + Environment.NewLine, Font, 0, 0, 4);
                    }
                }
            }
            _page.SelectionStart = 0;
            _page.ScrollToCaret();
        }

        /// <summary>Adds one paragraph with its own font, indent (first line / hanging) and space above.</summary>
        private void Append(string text, Font font, int indent, int hangingExtra, int spaceBefore)
        {
            int start = _page.TextLength;
            _page.AppendText(text);
            _page.Select(start, _page.TextLength - start);
            _page.SelectionFont = font;
            _page.SelectionIndent = indent;
            _page.SelectionHangingIndent = hangingExtra;
            _page.SelectionRightIndent = 8;
            _page.SelectionLength = 0;

            // RichTextBox has no "space before paragraph": a small blank line above does the job.
            if (spaceBefore > 0 && start > 0)
            {
                _page.Select(start, 0);
                using (Font gap = new Font(Font.FontFamily, Math.Max(2f, spaceBefore * 0.75f)))
                {
                    _page.SelectionFont = gap;
                    _page.SelectedText = Environment.NewLine;
                }
            }
            _page.Select(_page.TextLength, 0);
        }

        /// <summary>
        /// Opens a Windows screen. The clock and its windows are normally always-on-top, which would
        /// hide it - so drop that for every window of the app until this help window closes.
        /// </summary>
        private void OpenWindowsScreen(string target, string arguments)
        {
            try
            {
                ProcessStartInfo info = arguments == null ? new ProcessStartInfo(target) : new ProcessStartInfo(target, arguments);
                info.UseShellExecute = true;
                Process.Start(info);

                foreach (Form form in Application.OpenForms)
                {
                    if (!form.TopMost) continue;
                    _wasTopMost.Add(form);
                    form.TopMost = false;
                }
            }
            catch
            {
                MessageBox.Show(this, "That Windows screen could not be opened from here. Open Settings > Bluetooth & devices by hand.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            foreach (Form form in _wasTopMost)
            {
                if (!form.IsDisposed && form != this) form.TopMost = true;
            }
            _wasTopMost.Clear();
            base.OnFormClosed(e);
        }
    }
}
