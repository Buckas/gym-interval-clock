using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace GymClock.Link
{
    /// <summary>
    /// "How to set up Bluetooth between two PCs": the Windows steps (pair the PCs, join the
    /// Personal Area Network) and then the Link displays settings, with buttons that open the two
    /// Windows screens involved. Opened from the Link displays window.
    /// </summary>
    public sealed class BluetoothHelpForm : Form
    {
        /// <summary>The steps, as shown in the window and written in docs/plans/linked-displays.md section 6.3.</summary>
        public static readonly string[][] Steps =
        {
            new[]
            {
                "1.  Turn Bluetooth on  (both PCs)",
                "Open Settings > Bluetooth & devices and switch Bluetooth on. Leave this Settings page open on "
                + "both PCs while you pair them - a PC can only be found while it is open."
            },
            new[]
            {
                "2.  Pair the two PCs  (once only)",
                "On the SECOND PC choose Add device > Bluetooth, then pick the MAIN PC's name from the list. "
                + "A number appears on both screens: check they match, then choose Connect on one PC and Yes on the other.\n"
                + "If the main PC is not in the list: on the second PC, scroll down in Bluetooth & devices > Devices and set "
                + "\"Bluetooth devices discovery\" to Advanced, then try Add device again."
            },
            new[]
            {
                "3.  Join the Personal Area Network  (second PC)",
                "Press the button below to open \"Personal Area Network devices\" (or right-click the Bluetooth icon near "
                + "the clock and choose \"Join a Personal Area Network\"). Click the MAIN PC, then choose "
                + "Connect using > Direct connection. Do this each time the PCs are switched on."
            },
            new[]
            {
                "4.  MAIN PC - in Gym Interval Clock",
                "Press L, choose \"This is the MAIN PC\" and tick \"Also accept second screens over Bluetooth\". "
                + "The bottom of the screen shows LINK ON (NETWORK + BLUETOOTH) and the link code."
            },
            new[]
            {
                "5.  SECOND PC - in Gym Interval Clock",
                "Press L, choose \"This is a SECOND SCREEN\", type the link code, tick \"Connect by Bluetooth instead of "
                + "the network\" and pick the MAIN PC from the list. Connecting can take several seconds; the bottom of the "
                + "screen then shows FOLLOWING and the main PC's name."
            }
        };

        public const string Footnote =
            "Bluetooth reaches about 10 metres, so keep the two PCs in the same room. If it will not connect: check "
            + "Bluetooth is on at both PCs, that each PC lists the other as Paired, and that the main PC's screen says "
            + "(NETWORK + BLUETOOTH).";

        public BluetoothHelpForm()
        {
            Text = "How to set up Bluetooth between two PCs";
            Icon = AppIconProvider.GetIcon();
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            TopMost = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(14);

            int textWidth = Font.Height * 34;
            TableLayoutPanel root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };

            root.Controls.Add(new Label
            {
                Text = "Use Bluetooth where there is no network the two PCs can share. It is set up in Windows first, then in Gym Interval Clock.",
                AutoSize = true, MaximumSize = new Size(textWidth, 0), Margin = new Padding(3, 0, 3, 8)
            });

            Font bold = new Font(Font, FontStyle.Bold);
            Disposed += delegate { bold.Dispose(); };
            foreach (string[] step in Steps)
            {
                root.Controls.Add(new Label { Text = step[0], UseMnemonic = false, AutoSize = true, Font = bold, Margin = new Padding(3, 8, 3, 2) });
                root.Controls.Add(new Label
                {
                    Text = step[1].Replace("\n", Environment.NewLine), UseMnemonic = false, AutoSize = true,
                    MaximumSize = new Size(textWidth, 0), Margin = new Padding(22, 0, 3, 0)
                });
            }

            root.Controls.Add(new Label
            {
                Text = Footnote, UseMnemonic = false, AutoSize = true, ForeColor = SystemColors.GrayText,
                MaximumSize = new Size(textWidth, 0), Margin = new Padding(3, 12, 3, 0)
            });

            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0), WrapContents = false };
            Button settings = new Button { Text = "Open Windows Bluetooth settings", AutoSize = true };
            settings.Click += delegate { Open("ms-settings:bluetooth", null); };
            Button pan = new Button { Text = "Open Personal Area Network devices", AutoSize = true };
            // The same window as the Bluetooth tray icon's "Join a Personal Area Network".
            pan.Click += delegate { Open("explorer.exe", "shell:::{28803F59-3A75-4058-995F-4EE5503B023C}"); };
            Button close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK, MinimumSize = new Size(90, 0) };
            buttons.Controls.Add(settings);
            buttons.Controls.Add(pan);
            buttons.Controls.Add(close);
            root.Controls.Add(buttons);

            Controls.Add(root);
            AcceptButton = close;
            CancelButton = close;
        }

        private readonly System.Collections.Generic.List<Form> _wasTopMost = new System.Collections.Generic.List<Form>();

        /// <summary>
        /// The clock and its windows are normally always-on-top, which would hide the Windows screen just
        /// opened. Drop that for every window of the app until this help window closes.
        /// </summary>
        private void StepBehindWindows()
        {
            foreach (Form form in Application.OpenForms)
            {
                if (!form.TopMost) continue;
                _wasTopMost.Add(form);
                form.TopMost = false;
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

        private void Open(string target, string arguments)
        {
            try
            {
                ProcessStartInfo info = arguments == null ? new ProcessStartInfo(target) : new ProcessStartInfo(target, arguments);
                info.UseShellExecute = true;
                Process.Start(info);

                StepBehindWindows();
            }
            catch
            {
                MessageBox.Show(this, "That Windows screen could not be opened from here. Open Settings > Bluetooth & devices by hand.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
