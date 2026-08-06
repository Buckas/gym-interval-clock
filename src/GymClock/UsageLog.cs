using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GymClock
{
    /// <summary>
    /// Counts how much the app is used, to distinguish school use from commercial use.
    ///
    /// Aggregate totals only - no individual timestamps, nothing about the person, the
    /// computer, the network or the files. The app never uploads any of it; the totals
    /// are summarised into the licence request email, where the user can see them in
    /// the preview before choosing to send.
    ///
    /// The useful signal here is days used per month, not time of day. Schools stop in
    /// January; gyms do not. Out-of-hours counts are recorded too, but they flag
    /// Saturday sport and 6am squads just as readily as commercial use, so they mean
    /// far less on their own.
    /// </summary>
    public static class UsageLog
    {
        private const int MonthsReported = 18;

        private static DateTime _startedAt;
        private static bool _running;

        /// <summary>Call once when the app opens.</summary>
        public static void Begin()
        {
            _startedAt = DateTime.Now;
            _running = true;
        }

        /// <summary>Call once when the app closes. Safe to call twice.</summary>
        public static void End()
        {
            if (!_running) return;
            _running = false;

            try
            {
                Record record = Load();
                DateTime today = _startedAt.Date;

                int minutes = (int)Math.Round((DateTime.Now - _startedAt).TotalMinutes);
                if (minutes < 0) minutes = 0;              // clock changed mid-session
                if (minutes > 24 * 60) minutes = 24 * 60;  // left running overnight

                record.Sessions++;
                record.Minutes += minutes;

                if (IsSchoolHours(_startedAt)) record.SchoolHours++;
                else record.OutOfHours++;

                // After 7pm and before 6:30am are both rare in a school and routine in
                // a gym, so they are worth far more than the blanket "out of hours"
                // count above. 6:30 rather than 7:00 is the line that separates a
                // commercial 5:15/6:00 class slot from a before-school session.
                if (_startedAt.Hour >= 19) record.LateSessions++;
                else if (IsBeforeHalfSix(_startedAt)) record.EarlySessions++;

                // 25 December to 15 January. Every Victorian school is shut; this is a
                // commercial gym's busiest fortnight of the year.
                if (IsShutdownPeriod(_startedAt)) record.ShutdownSessions++;

                if (record.First == DateTime.MinValue) record.First = today;

                if (record.Last != today)
                {
                    record.Days++;
                    record.Last = today;

                    // Saturday and Sunday are counted apart on purpose. Saturday sport
                    // is ordinary school life; Sunday is where commercial use shows.
                    if (_startedAt.DayOfWeek == DayOfWeek.Saturday) record.Saturdays++;
                    else if (_startedAt.DayOfWeek == DayOfWeek.Sunday) record.Sundays++;

                    // Distinct ISO weeks. A school year is about 40 of them; a business
                    // runs all 52.
                    string week = WeekKey(today);
                    if (record.LastWeek != week)
                    {
                        record.Weeks++;
                        record.LastWeek = week;
                    }

                    if (IsShutdownPeriod(today)) record.ShutdownDays++;

                    string month = today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                    int count;
                    record.DaysByMonth[month] = record.DaysByMonth.TryGetValue(month, out count) ? count + 1 : 1;
                }

                record.Save();
            }
            catch
            {
                // Usage counting is never worth an error message.
            }
        }

        /// <summary>
        /// Monday to Friday, 7am to 5pm. Anything outside is counted separately, but
        /// see the note above about how weak that signal is on its own.
        /// </summary>
        /// <summary>
        /// The summer shutdown: 25 December to 15 January inclusive. Term 4 has ended
        /// and Term 1 has not started, so a school has no reason to be running classes.
        /// </summary>
        public static bool IsShutdownPeriod(DateTime when)
        {
            if (when.Month == 12 && when.Day >= 25) return true;
            return when.Month == 1 && when.Day <= 15;
        }

        /// <summary>Earlier than 6:30am.</summary>
        public static bool IsBeforeHalfSix(DateTime when)
        {
            return when.Hour < 6 || (when.Hour == 6 && when.Minute < 30);
        }

        private static string WeekKey(DateTime date)
        {
            return ISOWeek.GetYear(date).ToString("0000", CultureInfo.InvariantCulture)
                + "-W" + ISOWeek.GetWeekOfYear(date).ToString("00", CultureInfo.InvariantCulture);
        }

        public static bool IsSchoolHours(DateTime when)
        {
            if (when.DayOfWeek == DayOfWeek.Saturday || when.DayOfWeek == DayOfWeek.Sunday) return false;
            return when.Hour >= 7 && when.Hour < 17;
        }

        /// <summary>The counters as they stand. Used for the email and the assessment.</summary>
        public static UsageSnapshot Snapshot()
        {
            Record record = Load();

            UsageSnapshot snapshot = new UsageSnapshot();
            snapshot.First = record.First;
            snapshot.Last = record.Last;
            snapshot.Sessions = record.Sessions;
            snapshot.Minutes = record.Minutes;
            snapshot.SchoolHours = record.SchoolHours;
            snapshot.OutOfHours = record.OutOfHours;
            snapshot.WeekendDays = record.Saturdays + record.Sundays;
            snapshot.Saturdays = record.Saturdays;
            snapshot.Sundays = record.Sundays;
            snapshot.LateSessions = record.LateSessions;
            snapshot.EarlySessions = record.EarlySessions;
            snapshot.Weeks = record.Weeks;
            snapshot.ShutdownDays = record.ShutdownDays;
            snapshot.ShutdownSessions = record.ShutdownSessions;
            snapshot.Days = record.Days;
            snapshot.DaysByMonth = new Dictionary<string, int>(record.DaysByMonth);
            return snapshot;
        }

        /// <summary>The lines that go into the licence request email.</summary>
        public static string Summary()
        {
            try
            {
                Record record = Load();
                if (record.Sessions == 0) return "Usage        : first run";

                int hours = record.Minutes / 60;
                int classified = record.SchoolHours + record.OutOfHours;
                int percent = classified > 0 ? (int)Math.Round(100.0 * record.SchoolHours / classified) : 0;

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Usage        : {0} sessions over {1} day{2}, {3} hour{4} total",
                    record.Sessions, record.Days, record.Days == 1 ? string.Empty : "s",
                    hours, hours == 1 ? string.Empty : "s"));

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Pattern      : {0}% in school hours, {1} Sat, {2} Sun, {3} after 7pm, {4} before 6:30am",
                    percent, record.Saturdays, record.Sundays,
                    record.LateSessions, record.EarlySessions));

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Spread       : {0} distinct week{1}, {2} day{3} in the 25 Dec - 15 Jan shutdown",
                    record.Weeks, record.Weeks == 1 ? string.Empty : "s",
                    record.ShutdownDays, record.ShutdownDays == 1 ? string.Empty : "s"));

                sb.AppendLine("Since        : " + record.First.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                List<string> months = new List<string>(record.DaysByMonth.Keys);
                months.Sort();
                if (months.Count > MonthsReported) months.RemoveRange(0, months.Count - MonthsReported);

                List<string> parts = new List<string>();
                foreach (string month in months)
                {
                    parts.Add(month + " " + record.DaysByMonth[month].ToString(CultureInfo.InvariantCulture) + "d");
                }

                if (parts.Count > 0)
                {
                    sb.AppendLine("By month     : " + string.Join(", ", parts.ToArray()));
                }

                return sb.ToString().TrimEnd();
            }
            catch
            {
                return "Usage        : unavailable";
            }
        }

        // ------------------------------------------------------------ the record

        private class Record
        {
            public DateTime First = DateTime.MinValue;
            public DateTime Last = DateTime.MinValue;
            public int Sessions;
            public int Minutes;
            public int SchoolHours;
            public int OutOfHours;
            public int WeekendDays;      // kept for files written by earlier versions
            public int Saturdays;
            public int Sundays;
            public int LateSessions;
            public int EarlySessions;
            public int Weeks;
            public int ShutdownDays;
            public int ShutdownSessions;
            public string LastWeek = string.Empty;
            public int Days;
            public readonly Dictionary<string, int> DaysByMonth = new Dictionary<string, int>();

            public void Save()
            {
                string path = FilePath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Gym Interval Clock - local usage totals.");
                sb.AppendLine("# Aggregate counts only. The app never uploads this file.");
                sb.AppendLine("first=" + Text(First));
                sb.AppendLine("last=" + Text(Last));
                sb.AppendLine("sessions=" + Sessions.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("minutes=" + Minutes.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("schoolhours=" + SchoolHours.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("outofhours=" + OutOfHours.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("saturdays=" + Saturdays.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("sundays=" + Sundays.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("late=" + LateSessions.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("early=" + EarlySessions.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("weeks=" + Weeks.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("shutdowndays=" + ShutdownDays.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("shutdownsessions=" + ShutdownSessions.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("lastweek=" + (LastWeek ?? string.Empty));
                sb.AppendLine("days=" + Days.ToString(CultureInfo.InvariantCulture));

                List<string> months = new List<string>(DaysByMonth.Keys);
                months.Sort();
                foreach (string month in months)
                {
                    sb.AppendLine("m" + month + "=" + DaysByMonth[month].ToString(CultureInfo.InvariantCulture));
                }

                File.WriteAllText(path, sb.ToString());
            }

            private static string Text(DateTime value)
            {
                return value == DateTime.MinValue
                    ? string.Empty
                    : value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        private static Record Load()
        {
            Record record = new Record();

            try
            {
                string path = FilePath();
                if (!File.Exists(path)) return record;

                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    int equals = line.IndexOf('=');
                    if (equals <= 0) continue;

                    string key = line.Substring(0, equals).Trim();
                    string value = line.Substring(equals + 1).Trim();

                    if (key.Length == 8 && key[0] == 'm')
                    {
                        int months;
                        if (int.TryParse(value, out months)) record.DaysByMonth[key.Substring(1)] = months;
                        continue;
                    }

                    switch (key)
                    {
                        case "first": record.First = Date(value); break;
                        case "last": record.Last = Date(value); break;
                        case "sessions": record.Sessions = Number(value); break;
                        case "minutes": record.Minutes = Number(value); break;
                        case "schoolhours": record.SchoolHours = Number(value); break;
                        case "outofhours": record.OutOfHours = Number(value); break;
                        case "weekenddays": record.WeekendDays = Number(value); break;
                        case "saturdays": record.Saturdays = Number(value); break;
                        case "sundays": record.Sundays = Number(value); break;
                        case "late": record.LateSessions = Number(value); break;
                        case "early": record.EarlySessions = Number(value); break;
                        case "weeks": record.Weeks = Number(value); break;
                        case "shutdowndays": record.ShutdownDays = Number(value); break;
                        case "shutdownsessions": record.ShutdownSessions = Number(value); break;
                        case "lastweek": record.LastWeek = value; break;
                        case "days": record.Days = Number(value); break;
                    }
                }
            }
            catch { }

            return record;
        }

        private static string FilePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GymClock", "usage.txt");
        }

        private static DateTime Date(string value)
        {
            DateTime result;
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out result) ? result : DateTime.MinValue;
        }

        private static int Number(string value)
        {
            int result;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? result : 0;
        }
    }
}
