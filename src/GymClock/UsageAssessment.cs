using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GymClock
{
    /// <summary>The counters as they stand, for anything that wants to read them.</summary>
    public class UsageSnapshot
    {
        public DateTime First = DateTime.MinValue;
        public DateTime Last = DateTime.MinValue;
        public int Sessions;
        public int Minutes;
        public int SchoolHours;
        public int OutOfHours;
        public int WeekendDays;
        public int Saturdays;
        public int Sundays;
        public int LateSessions;
        public int EarlySessions;
        public int Weeks;
        public int ShutdownDays;
        public int ShutdownSessions;
        public int Days;
        public Dictionary<string, int> DaysByMonth = new Dictionary<string, int>();
    }

    /// <summary>
    /// Turns the usage counters into an education-or-commercial judgement.
    ///
    /// Deliberately conservative: every indicator is only counted when there is
    /// enough data to support it, and anything thin comes back UNCLASSIFIED rather
    /// than guessing. A wrong COMMERCIAL flag pointed at a teacher running Saturday
    /// sport costs more than a missed one.
    ///
    /// The result travels in a signed code, not as a word in an email body, because
    /// an email body is editable and a word is not evidence of anything.
    /// </summary>
    public static class UsageAssessment
    {
        /// <summary>
        /// Tamper-evidence only, NOT security. Someone who decompiles the app can
        /// recompute this. It exists to make casual editing of the numbers or the
        /// verdict detectable, which is the realistic threat.
        /// </summary>
        private const string ReportSecret = "GymClock-usage-report-v1";

        private const int MinimumSessions = 20;
        private const int MinimumDays = 10;
        private const int MinimumSpanDays = 30;

        public class Result
        {
            public string Band = "UNCLASSIFIED";
            public int Score;               // positive = education, negative = commercial
            public int Possible;            // how much was actually measurable
            public List<string> Reasons = new List<string>();

            /// <summary>
            /// Hard flags. These do not adjust the score - they cap the verdict,
            /// because they describe patterns a school simply does not produce.
            /// </summary>
            public List<string> Flags = new List<string>();

            public string Headline
            {
                get
                {
                    if (Band == "UNCLASSIFIED") return "UNCLASSIFIED (not enough use yet)";

                    string headline = string.Format(CultureInfo.InvariantCulture, "{0} ({1}{2} of {3})",
                        Band, Score >= 0 ? "+" : string.Empty, Score, Possible);

                    if (Flags.Count > 0)
                    {
                        headline += Flags.Count == 1 ? "  [1 HARD FLAG]"
                            : string.Format(CultureInfo.InvariantCulture, "  [{0} HARD FLAGS]", Flags.Count);
                    }

                    return headline;
                }
            }
        }

        public static Result Evaluate(UsageSnapshot usage)
        {
            Result result = new Result();
            if (usage == null) return result;

            int span = usage.Last > usage.First ? (int)(usage.Last - usage.First).TotalDays + 1 : 0;

            if (usage.Sessions < MinimumSessions || usage.Days < MinimumDays || span < MinimumSpanDays)
            {
                result.Reasons.Add(string.Format(CultureInfo.InvariantCulture,
                    "only {0} sessions over {1} days", usage.Sessions, usage.Days));
                return result;
            }

            // 1. Weeks used per year. A Victorian school year is about 40 term
            //    weeks; a business runs all 52. Needs roughly a year of history, so
            //    it contributes nothing to the first round of renewals.
            if (span >= 300 && usage.Weeks > 0)
            {
                double weeksPerYear = usage.Weeks / (span / 365.0);
                Weigh(result, 3, weeksPerYear <= 44.0, weeksPerYear >= 50.0,
                    string.Format(CultureInfo.InvariantCulture, "{0:0} weeks per year", weeksPerYear));
            }

            // 2. Sessions per active day. Eight periods is a full teaching day, so the
            //    commercial threshold sits above that - otherwise a PE teacher running
            //    the clock every period looks like a gym, which is precisely backwards.
            double perDay = (double)usage.Sessions / usage.Days;
            Weigh(result, 2, perDay <= 4.0, perDay > 8.0,
                string.Format(CultureInfo.InvariantCulture, "{0:0.0} sessions per active day", perDay));

            // 3. Days per active week. Dividing by active weeks rather than months
            //    keeps holidays out of the denominator, so this measures intensity
            //    while signal 1 measures continuity.
            if (usage.Weeks >= 4)
            {
                double daysPerWeek = (double)usage.Days / usage.Weeks;
                Weigh(result, 2, daysPerWeek <= 5.0, daysPerWeek >= 6.0,
                    string.Format(CultureInfo.InvariantCulture, "{0:0.0} days per active week", daysPerWeek));
            }

            // 4. Sunday. Saturday sport is ordinary school life and is deliberately
            //    NOT scored; Sunday is where commercial use shows itself.
            double sundayShare = (double)usage.Sundays / usage.Days;
            Weigh(result, 2, sundayShare <= 0.03, sundayShare >= 0.10,
                string.Format(CultureInfo.InvariantCulture, "{0:0}% of days on a Sunday", sundayShare * 100));

            // 5. Sessions starting after 7pm.
            double lateShare = (double)usage.LateSessions / usage.Sessions;
            Weigh(result, 2, lateShare <= 0.05, lateShare >= 0.20,
                string.Format(CultureInfo.InvariantCulture, "{0:0}% of sessions after 7pm", lateShare * 100));

            // 6. Sessions starting before 6:30am. This one does collide with rowing
            //    and swimming squads, which are genuinely school activities, so the
            //    neutral band is wide and the weight is modest - a coach trips it but
            //    stays comfortably in EDUCATION on the strength of the others.
            double earlyShare = (double)usage.EarlySessions / usage.Sessions;
            Weigh(result, 2, earlyShare <= 0.03, earlyShare >= 0.12,
                string.Format(CultureInfo.InvariantCulture, "{0:0}% of sessions before 6:30am", earlyShare * 100));

            // 7. The January test. Still the sharpest single signal in Australia.
            double drop;
            if (TryJanuaryDrop(usage, out drop))
            {
                Weigh(result, 3, drop >= 0.50, drop < 0.20,
                    string.Format(CultureInfo.InvariantCulture, "{0:0}% quieter in January", drop * 100));
            }

            if (result.Possible < 5)
            {
                result.Band = "UNCLASSIFIED";
                result.Reasons.Add("not enough measurable signals");
                return result;
            }

            // Scored as a proportion of what could actually be measured, so a verdict
            // reached on four signals is not treated as weaker than one reached on six.
            double ratio = (double)result.Score / result.Possible;

            if (ratio >= 0.60) result.Band = "EDUCATION";
            else if (ratio >= 0.20) result.Band = "EDUCATION-LIKELY";
            else if (ratio > -0.20) result.Band = "REVIEW";
            else if (ratio > -0.60) result.Band = "COMMERCIAL-LIKELY";
            else result.Band = "COMMERCIAL";

            ApplyHardFlags(usage, result);
            return result;
        }

        /// <summary>
        /// Two patterns that a school does not produce, whatever the weighted score
        /// says. Both carry a substantiveness bar rather than firing on a single
        /// occurrence, because one Sunday evening or one January afternoon is a
        /// teacher preparing for next term, not a business trading.
        /// </summary>
        private static void ApplyHardFlags(UsageSnapshot usage, Result result)
        {
            // Regular Sunday operation. Six or more Sundays AND a meaningful share of
            // all use - so a handful of Sunday evenings spent setting up a circuit does
            // not trip it, but trading every weekend does.
            double sundayShare = usage.Days > 0 ? (double)usage.Sundays / usage.Days : 0;
            if (usage.Sundays >= 6 && sundayShare >= 0.05)
            {
                result.Flags.Add(string.Format(CultureInfo.InvariantCulture,
                    "regular Sunday use ({0} Sundays, {1:0}% of all days)",
                    usage.Sundays, sundayShare * 100));
            }

            // Trading through the summer shutdown. Three or more separate days between
            // 25 December and 15 January - a planning session or two in early January
            // is ordinary, a fortnight of classes is not.
            if (usage.ShutdownDays >= 3)
            {
                result.Flags.Add(string.Format(CultureInfo.InvariantCulture,
                    "used on {0} days between 25 Dec and 15 Jan ({1} sessions)",
                    usage.ShutdownDays, usage.ShutdownSessions));
            }

            if (result.Flags.Count == 0) return;

            // One flag caps the verdict at COMMERCIAL-LIKELY, two make it COMMERCIAL.
            // The score and its reasons are left untouched and still reported, so any
            // disagreement between the two is visible rather than hidden.
            string capped = result.Flags.Count >= 2 ? "COMMERCIAL" : "COMMERCIAL-LIKELY";

            if (Rank(result.Band) > Rank(capped)) result.Band = capped;
        }

        /// <summary>Higher means more educational.</summary>
        private static int Rank(string band)
        {
            switch (band)
            {
                case "EDUCATION": return 5;
                case "EDUCATION-LIKELY": return 4;
                case "REVIEW": return 3;
                case "COMMERCIAL-LIKELY": return 2;
                case "COMMERCIAL": return 1;
                default: return 3;
            }
        }

        private static void Weigh(Result result, int weight, bool education, bool commercial, string reason)
        {
            result.Possible += weight;

            if (education) result.Score += weight;
            else if (commercial) result.Score -= weight;

            result.Reasons.Add(reason);
        }

        /// <summary>
        /// How much quieter January is than a typical month. Needs a January on record,
        /// so it contributes nothing to the first year's renewals.
        /// </summary>
        private static bool TryJanuaryDrop(UsageSnapshot usage, out double drop)
        {
            drop = 0;

            List<int> others = new List<int>();
            int january = -1;

            foreach (KeyValuePair<string, int> entry in usage.DaysByMonth)
            {
                if (entry.Key.Length == 7 && entry.Key.Substring(5) == "01")
                {
                    if (entry.Value > january) january = entry.Value;
                }
                else
                {
                    others.Add(entry.Value);
                }
            }

            if (january < 0 || others.Count < 3) return false;

            others.Sort();
            double median = others[others.Count / 2];
            if (median <= 0) return false;

            drop = 1.0 - (january / median);
            if (drop < 0) drop = 0;
            return true;
        }

        // -------------------------------------------------------------- the code

        /// <summary>
        /// One line carrying the verdict and every number behind it, with an HMAC so
        /// that editing either the numbers or the verdict makes them disagree.
        /// Decoded with: GymClock.KeyGen usagecheck --code U1....
        /// </summary>
        public static string Code(UsageSnapshot usage, Result result)
        {
            try
            {
                List<string> months = new List<string>();
                List<string> keys = new List<string>(usage.DaysByMonth.Keys);
                keys.Sort();
                foreach (string key in keys)
                {
                    months.Add(key + "=" + usage.DaysByMonth[key].ToString(CultureInfo.InvariantCulture));
                }

                string payload = string.Join("|", new string[]
                {
                    "2",
                    "band=" + result.Band,
                    "score=" + result.Score.ToString(CultureInfo.InvariantCulture),
                    "possible=" + result.Possible.ToString(CultureInfo.InvariantCulture),
                    "sessions=" + usage.Sessions.ToString(CultureInfo.InvariantCulture),
                    "minutes=" + usage.Minutes.ToString(CultureInfo.InvariantCulture),
                    "days=" + usage.Days.ToString(CultureInfo.InvariantCulture),
                    "weeks=" + usage.Weeks.ToString(CultureInfo.InvariantCulture),
                    "shutdowndays=" + usage.ShutdownDays.ToString(CultureInfo.InvariantCulture),
                    "shutdownsessions=" + usage.ShutdownSessions.ToString(CultureInfo.InvariantCulture),
                    "flags=" + string.Join(";", result.Flags.ToArray()),
                    "saturdays=" + usage.Saturdays.ToString(CultureInfo.InvariantCulture),
                    "sundays=" + usage.Sundays.ToString(CultureInfo.InvariantCulture),
                    "late=" + usage.LateSessions.ToString(CultureInfo.InvariantCulture),
                    "early=" + usage.EarlySessions.ToString(CultureInfo.InvariantCulture),
                    "inhours=" + usage.SchoolHours.ToString(CultureInfo.InvariantCulture),
                    "outofhours=" + usage.OutOfHours.ToString(CultureInfo.InvariantCulture),
                    "first=" + Text(usage.First),
                    "last=" + Text(usage.Last),
                    "months=" + string.Join(",", months.ToArray())
                });

                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                byte[] mac;
                using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(ReportSecret)))
                {
                    mac = hmac.ComputeHash(bytes);
                }

                return "U1." + Base64Url(bytes) + "." + Base64Url(Truncate(mac, 6));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static byte[] Truncate(byte[] data, int length)
        {
            byte[] result = new byte[length];
            Array.Copy(data, result, length);
            return result;
        }

        private static string Text(DateTime value)
        {
            return value == DateTime.MinValue
                ? string.Empty
                : value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
