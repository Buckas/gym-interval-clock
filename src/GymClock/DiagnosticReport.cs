using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace GymClock
{
    /// <summary>
    /// Builds a single readable text file the user can send when a licence question
    /// needs discussing.
    ///
    /// Written to be read by the person sending it. Everything in it is either
    /// already visible in the app or a count of app usage - no usernames, no network,
    /// no file paths belonging to the user. If somebody opens it before emailing it,
    /// there should be nothing in there that surprises them.
    /// </summary>
    public static class DiagnosticReport
    {
        public static string SuggestedFileName()
        {
            return "GymClock-report-" + Licensing.MachineId() + "-"
                + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".txt";
        }

        public static string Build(TimerSettings settings)
        {
            StringBuilder sb = new StringBuilder();
            LicenceStatus status = Licensing.Current;
            UsageSnapshot usage = UsageLog.Snapshot();
            UsageAssessment.Result assessment = UsageAssessment.Evaluate(usage);

            Heading(sb, "GYM INTERVAL CLOCK - SUPPORT REPORT");
            sb.AppendLine("Generated    : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            sb.AppendLine("App version  : " + Application.ProductVersion);
            sb.AppendLine("Computer ID  : " + Licensing.MachineId());
            sb.AppendLine();
            sb.AppendLine("This file contains counts of how the app has been used, plus your");
            sb.AppendLine("licence details. It does not contain your username, your network, your");
            sb.AppendLine("files or anything else about this computer. You are welcome to read it");
            sb.AppendLine("before you send it.");
            sb.AppendLine();

            Heading(sb, "LICENCE");
            sb.AppendLine("Status       : " + status.State);
            if (status.Licence != null)
            {
                sb.AppendLine("Licensed to  : " + status.Licence.Name);
                sb.AppendLine("Expires      : " + (status.Licence.Expires.HasValue
                    ? status.Licence.Expires.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : "never"));
                sb.AppendLine("Machine lock : " + (status.Licence.IsMachineLocked ? "yes" : "any computer"));
                sb.AppendLine("Issued       : " + status.Licence.Issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.AppendLine("Licensed to  : no licence installed");
            }
            sb.AppendLine("Free use to  : " + Licensing.FreeUseEndsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(status.Detail)) sb.AppendLine("Note         : " + status.Detail);
            sb.AppendLine();

            Heading(sb, "USAGE");
            sb.AppendLine(UsageLog.Summary());
            sb.AppendLine("Licence type : " + assessment.Headline);
            if (assessment.Reasons.Count > 0)
            {
                foreach (string reason in assessment.Reasons) sb.AppendLine("  - " + reason);
            }
            foreach (string flag in assessment.Flags) sb.AppendLine("Flag         : " + flag);
            sb.AppendLine();

            Heading(sb, "DAYS USED PER MONTH");
            if (usage.DaysByMonth.Count == 0)
            {
                sb.AppendLine("(nothing recorded yet)");
            }
            else
            {
                List<string> months = new List<string>(usage.DaysByMonth.Keys);
                months.Sort();
                foreach (string month in months)
                {
                    int days = usage.DaysByMonth[month];
                    sb.AppendLine("  " + month + "  " + days.ToString(CultureInfo.InvariantCulture).PadLeft(3)
                        + " day" + (days == 1 ? string.Empty : "s") + "  " + new string('#', Math.Min(days, 31)));
                }
            }
            sb.AppendLine();

            Heading(sb, "SETTINGS");
            if (settings != null)
            {
                sb.AppendLine("Plan         : " + settings.EffectivePlan().Summary());
                sb.AppendLine("Rounds       : " + settings.EffectivePlan().RoundCount.ToString(CultureInfo.InvariantCulture)
                    + (settings.Rounds == 0 ? " (repeating continuously)" : string.Empty));
                sb.AppendLine("Prep         : " + settings.PrepSeconds.ToString(CultureInfo.InvariantCulture) + "s");
                sb.AppendLine("Wording      : " + settings.WorkLabel + " / " + settings.RestLabel);
                sb.AppendLine("Sound        : " + (settings.SoundEnabled ? "on" : "muted"));
                sb.AppendLine("Stations     : " + (settings.Stations.Count == 0
                    ? "none" : string.Join(", ", settings.Stations.ToArray())));
            }
            sb.AppendLine("Settings file: " + TimerSettings.FilePath);
            sb.AppendLine();

            Heading(sb, "VERIFICATION CODE");
            sb.AppendLine("Paste this into the key generator to confirm the figures above.");
            sb.AppendLine();
            sb.AppendLine(UsageAssessment.Code(usage, assessment));
            sb.AppendLine();

            return sb.ToString();
        }

        /// <summary>Asks where to save, writes the file, then offers to open the folder.</summary>
        public static void SaveInteractive(IWin32Window owner, TimerSettings settings)
        {
            string path;

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Save support report";
                dialog.FileName = SuggestedFileName();
                dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                path = dialog.FileName;
            }

            try
            {
                File.WriteAllText(path, Build(settings));
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "The report could not be saved: " + ex.Message,
                    "Save support report", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult open = MessageBox.Show(owner,
                "Report saved to:" + Environment.NewLine + path + Environment.NewLine + Environment.NewLine
                + "Attach it to an email to " + Licensing.ContactDetails + "."
                + Environment.NewLine + Environment.NewLine
                + "Open the folder now?",
                "Save support report", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (open != DialogResult.Yes) return;

            try
            {
                System.Diagnostics.ProcessStartInfo info =
                    new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"");
                info.UseShellExecute = true;
                System.Diagnostics.Process.Start(info);
            }
            catch { }
        }

        private static void Heading(StringBuilder sb, string text)
        {
            sb.AppendLine(text);
            sb.AppendLine(new string('-', text.Length));
        }
    }
}
