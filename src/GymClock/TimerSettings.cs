using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace GymClock
{
    /// <summary>A work/rest pairing that can be applied with a single key press.</summary>
    public class Preset
    {
        public int Work;
        public int Rest;
        public int Rounds;   // 0 = leave the current round count alone

        public Preset(int work, int rest, int rounds)
        {
            Work = work;
            Rest = rest;
            Rounds = rounds;
        }

        /// <summary>Short label used on buttons and in the on-screen hint bar.</summary>
        public string Label
        {
            get
            {
                return Rounds > 0
                    ? string.Format(CultureInfo.InvariantCulture, "{0}/{1} x{2}", Work, Rest, Rounds)
                    : string.Format(CultureInfo.InvariantCulture, "{0}/{1}", Work, Rest);
            }
        }

        /// <summary>Format written to the settings file, e.g. 45/15x10</summary>
        public override string ToString()
        {
            return Rounds > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}/{1}x{2}", Work, Rest, Rounds)
                : string.Format(CultureInfo.InvariantCulture, "{0}/{1}", Work, Rest);
        }

        public static bool TryParse(string text, out Preset preset)
        {
            preset = null;
            if (string.IsNullOrEmpty(text)) return false;

            text = text.Trim().ToLowerInvariant().Replace("s", string.Empty);
            string[] halves = text.Split(new[] { '/', '-', ':' }, 2);
            if (halves.Length != 2) return false;

            int work, rest, rounds = 0;
            if (!int.TryParse(halves[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out work)) return false;

            string second = halves[1].Trim();
            int xAt = second.IndexOf('x');
            if (xAt >= 0)
            {
                int.TryParse(second.Substring(xAt + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out rounds);
                second = second.Substring(0, xAt).Trim();
            }
            if (!int.TryParse(second, NumberStyles.Integer, CultureInfo.InvariantCulture, out rest)) return false;

            preset = new Preset(Clamp(work, 1, 3600), Clamp(rest, 0, 3600), Clamp(rounds, 0, 999));
            return true;
        }

        private static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }

    /// <summary>
    /// Everything the clock needs, stored as a human-readable key=value text file
    /// so it can also be edited with Notepad if you would rather not use the dialog.
    /// </summary>
    public class TimerSettings
    {
        public int PrepSeconds = 10;
        public int WorkSeconds = 45;
        public int RestSeconds = 15;
        public int Rounds = 10;                  // 0 = repeat continuously

        /// <summary>
        /// Optional variable structure, e.g. "45/15x2+50/10x4". When set it overrides
        /// WorkSeconds, RestSeconds and the round count. Empty means a plain uniform
        /// session built from those three, which is what every older settings file has.
        /// </summary>
        public string Plan = string.Empty;
        public bool RestAfterFinalRound = false;
        public bool SoundEnabled = true;
        public double Volume = 0.85;
        public bool AlwaysOnTop = true;
        public bool ShowClock = true;
        public string ClockFormat = "h:mm:ss tt";
        public string WorkLabel = "WORK";
        public string RestLabel = "REST";

        public string WorkColour = "#0F8A46";
        public string RestColour = "#D9822B";
        public string PrepColour = "#1E63D0";
        public string PausedColour = "#333B47";
        public string DoneColour = "#5B4B9E";
        public string IdleColour = "#1A1E25";

        /// <summary>Optional station / exercise names, shown one per work round.</summary>
        public List<string> Stations = new List<string>();

        public List<Preset> Presets = new List<Preset>();

        public TimerSettings()
        {
            Presets.Add(new Preset(45, 15, 10));
            Presets.Add(new Preset(40, 20, 10));
            Presets.Add(new Preset(30, 30, 10));
            Presets.Add(new Preset(20, 10, 8));
            Presets.Add(new Preset(60, 15, 8));
        }

        /// <summary>
        /// The plan actually in force: the custom one if there is a valid one, and a
        /// single-block uniform plan otherwise.
        /// </summary>
        public IntervalPlan EffectivePlan()
        {
            if (!string.IsNullOrEmpty(Plan))
            {
                IntervalPlan parsed = IntervalPlan.Parse(Plan);
                if (!parsed.IsEmpty) return parsed;
            }

            return IntervalPlan.Uniform(WorkSeconds, RestSeconds, Rounds < 1 ? 1 : Rounds);
        }

        public TimerSettings Clone()
        {
            TimerSettings c = new TimerSettings();
            c.PrepSeconds = PrepSeconds;
            c.WorkSeconds = WorkSeconds;
            c.RestSeconds = RestSeconds;
            c.Rounds = Rounds;
            c.Plan = Plan;
            c.RestAfterFinalRound = RestAfterFinalRound;
            c.SoundEnabled = SoundEnabled;
            c.Volume = Volume;
            c.AlwaysOnTop = AlwaysOnTop;
            c.ShowClock = ShowClock;
            c.ClockFormat = ClockFormat;
            c.WorkLabel = WorkLabel;
            c.RestLabel = RestLabel;
            c.WorkColour = WorkColour;
            c.RestColour = RestColour;
            c.PrepColour = PrepColour;
            c.PausedColour = PausedColour;
            c.DoneColour = DoneColour;
            c.IdleColour = IdleColour;
            c.Stations = new List<string>(Stations);
            c.Presets = new List<Preset>();
            foreach (Preset p in Presets) c.Presets.Add(new Preset(p.Work, p.Rest, p.Rounds));
            return c;
        }

        // ---------------------------------------------------------------- file

        /// <summary>
        /// Portable mode: if gymclock.settings.txt sits next to the .exe it wins.
        /// Otherwise settings live in %AppData%\GymClock\settings.txt
        /// </summary>
        public static string FilePath
        {
            get
            {
                try
                {
                    string beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gymclock.settings.txt");
                    if (File.Exists(beside)) return beside;
                }
                catch { }

                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GymClock");
                return Path.Combine(dir, "settings.txt");
            }
        }

        public static TimerSettings Load()
        {
            TimerSettings s = new TimerSettings();
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return s;

                bool presetsSeen = false;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "prep": s.PrepSeconds = ReadInt(value, s.PrepSeconds, 0, 300); break;
                        case "work": s.WorkSeconds = ReadInt(value, s.WorkSeconds, 1, 3600); break;
                        case "rest": s.RestSeconds = ReadInt(value, s.RestSeconds, 0, 3600); break;
                        case "rounds": s.Rounds = ReadInt(value, s.Rounds, 0, 999); break;
                        case "plan": s.Plan = value; break;
                        case "restafterfinalround": s.RestAfterFinalRound = ReadBool(value, s.RestAfterFinalRound); break;
                        case "sound": s.SoundEnabled = ReadBool(value, s.SoundEnabled); break;
                        case "volume": s.Volume = ReadDouble(value, s.Volume, 0.05, 1.0); break;
                        case "alwaysontop": s.AlwaysOnTop = ReadBool(value, s.AlwaysOnTop); break;
                        case "showclock": s.ShowClock = ReadBool(value, s.ShowClock); break;
                        case "clockformat": if (value.Length > 0) s.ClockFormat = value; break;
                        case "worklabel": s.WorkLabel = value; break;
                        case "restlabel": s.RestLabel = value; break;
                        case "workcolour":
                        case "workcolor": s.WorkColour = value; break;
                        case "restcolour":
                        case "restcolor": s.RestColour = value; break;
                        case "prepcolour":
                        case "prepcolor": s.PrepColour = value; break;
                        case "pausedcolour":
                        case "pausedcolor": s.PausedColour = value; break;
                        case "donecolour":
                        case "donecolor": s.DoneColour = value; break;
                        case "idlecolour":
                        case "idlecolor": s.IdleColour = value; break;
                        case "stations": s.Stations = SplitList(value); break;
                        case "presets":
                            List<Preset> parsed = new List<Preset>();
                            foreach (string item in SplitList(value))
                            {
                                Preset p;
                                if (Preset.TryParse(item, out p)) parsed.Add(p);
                            }
                            if (parsed.Count > 0) { s.Presets = parsed; presetsSeen = true; }
                            break;
                    }
                }

                if (!presetsSeen && s.Presets.Count == 0) s.Presets.Add(new Preset(45, 15, 10));
            }
            catch
            {
                // A corrupt or unreadable file should never stop the clock from running.
                s = new TimerSettings();
            }
            return s;
        }

        public void Save()
        {
            try
            {
                string path = FilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Gym Interval Clock settings");
                sb.AppendLine("# Edit with Notepad or use the in-app settings dialog (press S).");
                sb.AppendLine("# Times are in seconds. rounds=0 means keep looping until stopped.");
                sb.AppendLine();
                sb.AppendLine("prep=" + PrepSeconds.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("work=" + WorkSeconds.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("rest=" + RestSeconds.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("rounds=" + Rounds.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();
                sb.AppendLine("# Optional variable structure. Blocks are work/rest x repeats,");
                sb.AppendLine("# joined with +. Overrides work, rest and the round count above.");
                sb.AppendLine("#   plan=45/15x2+50/10x4     two rounds of 45/15, then four of 50/10");
                sb.AppendLine("# Leave blank for a plain uniform session.");
                sb.AppendLine("plan=" + Plan);
                sb.AppendLine("restafterfinalround=" + (RestAfterFinalRound ? "true" : "false"));
                sb.AppendLine("sound=" + (SoundEnabled ? "true" : "false"));
                sb.AppendLine("volume=" + Volume.ToString("0.00", CultureInfo.InvariantCulture));
                sb.AppendLine("alwaysontop=" + (AlwaysOnTop ? "true" : "false"));
                sb.AppendLine("showclock=" + (ShowClock ? "true" : "false"));
                sb.AppendLine("clockformat=" + ClockFormat);
                sb.AppendLine("worklabel=" + WorkLabel);
                sb.AppendLine("restlabel=" + RestLabel);
                sb.AppendLine();
                sb.AppendLine("# Colours: #RRGGBB or a .NET colour name");
                sb.AppendLine("workcolour=" + WorkColour);
                sb.AppendLine("restcolour=" + RestColour);
                sb.AppendLine("prepcolour=" + PrepColour);
                sb.AppendLine("pausedcolour=" + PausedColour);
                sb.AppendLine("donecolour=" + DoneColour);
                sb.AppendLine("idlecolour=" + IdleColour);
                sb.AppendLine();
                sb.AppendLine("# Optional station names, shown one per work round (comma separated)");
                sb.AppendLine("stations=" + string.Join(",", Stations.ToArray()));
                sb.AppendLine();
                sb.AppendLine("# Quick presets, applied with keys 1-9. Format work/restxrounds");
                List<string> ps = new List<string>();
                foreach (Preset p in Presets) ps.Add(p.ToString());
                sb.AppendLine("presets=" + string.Join(",", ps.ToArray()));

                File.WriteAllText(path, sb.ToString());
            }
            catch
            {
                // Read-only location (e.g. locked-down SOE) - carry on with in-memory settings.
            }
        }

        // ------------------------------------------------------------- helpers

        public Color WorkBg { get { return ParseColour(WorkColour, Color.FromArgb(15, 138, 70)); } }
        public Color RestBg { get { return ParseColour(RestColour, Color.FromArgb(217, 130, 43)); } }
        public Color PrepBg { get { return ParseColour(PrepColour, Color.FromArgb(30, 99, 208)); } }
        public Color PausedBg { get { return ParseColour(PausedColour, Color.FromArgb(51, 59, 71)); } }
        public Color DoneBg { get { return ParseColour(DoneColour, Color.FromArgb(91, 75, 158)); } }
        public Color IdleBg { get { return ParseColour(IdleColour, Color.FromArgb(26, 30, 37)); } }

        public static Color ParseColour(string text, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return fallback;
                string s = text.Trim();
                if (s.StartsWith("#")) s = s.Substring(1);

                if (s.Length == 6 && IsHex(s))
                {
                    return Color.FromArgb(255,
                        Convert.ToInt32(s.Substring(0, 2), 16),
                        Convert.ToInt32(s.Substring(2, 2), 16),
                        Convert.ToInt32(s.Substring(4, 2), 16));
                }

                Color named = Color.FromName(text.Trim());
                return named.IsKnownColor ? named : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static bool IsHex(string s)
        {
            foreach (char c in s)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }

        public static List<string> SplitList(string value)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(value)) return list;
            foreach (string part in value.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = part.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        private static int ReadInt(string value, int fallback, int min, int max)
        {
            int v;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return fallback;
            return v < min ? min : (v > max ? max : v);
        }

        private static double ReadDouble(string value, double fallback, double min, double max)
        {
            double v;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return fallback;
            return v < min ? min : (v > max ? max : v);
        }

        private static bool ReadBool(string value, bool fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            switch (value.Trim().ToLowerInvariant())
            {
                case "true":
                case "yes":
                case "1":
                case "on": return true;
                case "false":
                case "no":
                case "0":
                case "off": return false;
                default: return fallback;
            }
        }
    }
}
