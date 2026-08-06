using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace GymClock
{
    public enum LicenceState
    {
        /// <summary>A valid signed licence is installed.</summary>
        Licensed,

        /// <summary>Free-use period, still running normally.</summary>
        TrialActive,

        /// <summary>Free-use period, inside the final warning window.</summary>
        TrialEnding,

        /// <summary>Free-use period is over and no valid licence is installed.</summary>
        TrialExpired,

        /// <summary>An installed licence has expired, but is inside the grace window.</summary>
        LicenceGrace,

        /// <summary>An installed licence expired beyond the grace window.</summary>
        LicenceExpired,

        /// <summary>The system clock has been wound back to escape expiry.</summary>
        ClockRolledBack,

        /// <summary>This build has no signing key compiled in - a packaging mistake.</summary>
        NotConfigured
    }

    public class Licence
    {
        public string Name = string.Empty;
        public DateTime? Expires;            // null = perpetual
        public string Machine = "*";         // "*" = any machine
        public DateTime Issued;

        public bool IsMachineLocked
        {
            get { return !string.IsNullOrEmpty(Machine) && Machine != "*"; }
        }
    }

    public class LicenceStatus
    {
        public LicenceState State = LicenceState.TrialActive;
        public Licence Licence;
        public int DaysRemaining;
        public string Detail = string.Empty;

        /// <summary>False means the app must refuse to start.</summary>
        public bool CanRun
        {
            get
            {
                return State == LicenceState.Licensed
                    || State == LicenceState.LicenceGrace
                    || State == LicenceState.TrialActive
                    || State == LicenceState.TrialEnding;
            }
        }
    }

    /// <summary>
    /// Runs unlicensed until <see cref="FreeUseEndsOn"/>, with an on-screen warning
    /// from <see cref="WarningStartsOn"/>. After that a licence key is required.
    ///
    /// Licences are ECDSA P-256 signed, so the private key never ships with the app -
    /// somebody who decompiles this exe still cannot forge a licence. They could of
    /// course patch the check out; this is a deterrent against casual sharing, not
    /// protection against a determined attacker.
    /// </summary>
    public static class Licensing
    {
        /// <summary>
        /// Before this date the app says nothing at all about licensing.
        /// From this date it shows an unlicensed warning on the clock display.
        /// </summary>
        public static readonly DateTime WarningStartsOn = new DateTime(2027, 4, 1);

        /// <summary>
        /// The last day the app will run without a licence key. From the following
        /// day (1 May 2027) it will not start until a key is entered.
        /// Compiled in, not configurable.
        /// </summary>
        public static readonly DateTime FreeUseEndsOn = new DateTime(2027, 4, 30);

        /// <summary>
        /// How long an expired licence keeps working while a renewal is chased up.
        /// Without this, a slow reply from the author kills someone's lesson.
        /// </summary>
        public const int GraceDays = 30;

        /// <summary>
        /// Your public key, as printed by: GymClock.KeyGen newkeys
        /// The matching private key must NEVER be committed or shipped.
        /// </summary>
        private const string PublicKeyBase64 =
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+A/r5vVGvC58oxzkGoLsXiwqsc/WRgbJ21yz2STw66gChYLbuISVS1RBeqxaHKmBKMs6DKZ4R3B8q1gcmLR/YQ==";

        /// <summary>
        /// Compiled-in fallback address. Change this when you cut a new version.
        /// </summary>
        public const string DefaultContactDetails = "chris.bucknell@outlook.com.au";

        /// <summary>
        /// The address the app tells users to write to.
        ///
        /// Copies already in circulation cannot be recompiled, so this prefers an
        /// address published at contact.txt on the licence website and cached
        /// locally. That means a change of address reaches every copy already out
        /// there, not just newly built ones, and keeps working offline afterwards.
        /// </summary>
        public static string ContactDetails
        {
            get
            {
                if (_contactCache != null) return _contactCache;

                string stored = ReadStoredContact();
                _contactCache = IsPlausibleAddress(stored) ? stored : DefaultContactDetails;
                return _contactCache;
            }
        }

        private static string _contactCache;

        /// <summary>
        /// Caches an address published on the website. Returns false, and changes
        /// nothing, if it does not look like a plain email address.
        /// </summary>
        public static bool TrySetContactOverride(string candidate)
        {
            string trimmed = FirstLine(candidate);
            if (!IsPlausibleAddress(trimmed)) return false;
            if (string.Equals(trimmed, ContactDetails, StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key != null) key.SetValue("ct", trimmed);
                }
            }
            catch { }

            _contactCache = trimmed;
            return true;
        }

        private static string ReadStoredContact()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key != null) return key.GetValue("ct") as string;
                }
            }
            catch { }

            return null;
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            string[] lines = text.Replace("\r", "\n").Split('\n');
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith("#")) return trimmed;
            }

            return null;
        }

        /// <summary>
        /// Deliberately strict. This value ends up in a mailto: URL, so anything that
        /// could smuggle in extra headers - a query separator, a second recipient, a
        /// line break - is rejected outright rather than escaped and hoped for.
        /// </summary>
        private static bool IsPlausibleAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;
            if (address.Length < 6 || address.Length > 120) return false;

            int at = address.IndexOf('@');
            if (at < 1 || at != address.LastIndexOf('@')) return false;
            if (address.IndexOf('.', at) < at + 2) return false;
            if (address.EndsWith(".") || address.EndsWith("@")) return false;

            foreach (char c in address)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) return false;
                if ("?&,;:<>\"'\\()[]".IndexOf(c) >= 0) return false;
            }

            return true;
        }

        public const string ProductName = "Gym Interval Clock";
        public const string Copyright = "Copyright (c) 2026 Chris Bucknell and James Vella. All rights reserved.";

        /// <summary>
        /// The compact notice shown in the corner of the clock display. The (c) is a
        /// \u00A9 escape rather than a literal character so the source file stays
        /// pure ASCII and cannot be mangled by an encoding mismatch on build.
        /// </summary>
        public const string ShortCopyright = "\u00A9 C. Bucknell & J. Vella 2026";

        private const string LicencePrefix = "GCL1.";
        private const string RegistryPath = @"Software\GymClock";

        private static LicenceStatus _cached;

        /// <summary>Cached status, so we are not verifying a signature on every repaint.</summary>
        public static LicenceStatus Current
        {
            get { return _cached ?? (_cached = Evaluate()); }
        }

        public static LicenceStatus Refresh()
        {
            _cached = Evaluate();
            return _cached;
        }

        // ------------------------------------------------------------ evaluation

        private static LicenceStatus Evaluate()
        {
            LicenceStatus status = new LicenceStatus();
            DateTime today = DateTime.Now.Date;

            RecordRun(today);

            if (PublicKeyBase64 == "PASTE-YOUR-PUBLIC-KEY-HERE")
            {
                // Fail loudly rather than shipping a build nobody can ever licence.
                status.State = LicenceState.NotConfigured;
                status.Detail = "This build was compiled without a licence signing key.";
                return status;
            }

            // Winding the clock back only matters as a way to escape the cutoff.
            if (LastRunRecorded() > FreeUseEndsOn && today <= FreeUseEndsOn)
            {
                status.State = LicenceState.ClockRolledBack;
                status.Detail = "The system clock appears to have been set back.";
                return status;
            }

            string licenceText = ReadStoredLicence();
            if (!string.IsNullOrEmpty(licenceText))
            {
                Licence licence;
                string error;
                if (TryValidate(licenceText, out licence, out error))
                {
                    if (licence.Expires.HasValue && licence.Expires.Value.Date < today)
                    {
                        status.Licence = licence;
                        int daysOver = (int)(today - licence.Expires.Value.Date).TotalDays;

                        status.State = daysOver <= GraceDays
                            ? LicenceState.LicenceGrace
                            : LicenceState.LicenceExpired;

                        status.DaysRemaining = GraceDays - daysOver;
                        status.Detail = "The licence expired on "
                            + licence.Expires.Value.ToString("d MMMM yyyy", CultureInfo.CurrentCulture) + ".";
                        return status;
                    }

                    status.State = LicenceState.Licensed;
                    status.Licence = licence;
                    status.DaysRemaining = licence.Expires.HasValue
                        ? (int)(licence.Expires.Value.Date - today).TotalDays
                        : int.MaxValue;
                    return status;
                }

                status.Detail = "The installed licence is not valid: " + error;
            }

            // Inclusive: on the final day this reads "1 day left", not "0".
            status.DaysRemaining = (int)(FreeUseEndsOn.Date - today).TotalDays + 1;

            if (today > FreeUseEndsOn.Date)
            {
                status.State = LicenceState.TrialExpired;
                return status;
            }

            status.State = today >= WarningStartsOn.Date
                ? LicenceState.TrialEnding      // April 2027: warn on screen
                : LicenceState.TrialActive;     // before then: stay silent
            return status;
        }

        // ---------------------------------------------------------- verification

        /// <summary>
        /// Licence format:  GCL1.[base64url payload].[base64url signature]
        /// Payload:         1|name|expires|machine|issued
        /// </summary>
        public static bool TryValidate(string licenceText, out Licence licence, out string error)
        {
            licence = null;
            error = string.Empty;

            try
            {
                string text = (licenceText ?? string.Empty).Trim();

                // Tolerate keys that arrive with line breaks from an email client.
                text = text.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace(" ", string.Empty);

                if (!text.StartsWith(LicencePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    error = "it does not look like a Gym Clock licence key";
                    return false;
                }

                string[] parts = text.Substring(LicencePrefix.Length).Split('.');
                if (parts.Length != 2)
                {
                    error = "the key is incomplete or has been truncated";
                    return false;
                }

                byte[] payload = FromBase64Url(parts[0]);
                byte[] signature = FromBase64Url(parts[1]);

                using (ECDsa ecdsa = ECDsa.Create())
                {
                    int consumed;
                    ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyBase64), out consumed);

                    if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256))
                    {
                        error = "the signature does not match - the key may have been altered";
                        return false;
                    }
                }

                string[] fields = Encoding.UTF8.GetString(payload).Split('|');
                if (fields.Length < 5 || fields[0] != "1")
                {
                    error = "the key uses an unrecognised format";
                    return false;
                }

                Licence result = new Licence();
                result.Name = fields[1];
                result.Machine = string.IsNullOrEmpty(fields[3]) ? "*" : fields[3];

                if (!string.Equals(fields[2], "never", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime expires;
                    if (!DateTime.TryParseExact(fields[2], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out expires))
                    {
                        error = "the expiry date in the key is unreadable";
                        return false;
                    }
                    result.Expires = expires;
                }

                DateTime issued;
                if (DateTime.TryParseExact(fields[4], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out issued))
                {
                    result.Issued = issued;
                }

                if (result.IsMachineLocked &&
                    !string.Equals(result.Machine, MachineId(), StringComparison.OrdinalIgnoreCase))
                {
                    error = "this key was issued for a different computer";
                    return false;
                }

                licence = result;
                return true;
            }
            catch (Exception ex)
            {
                error = "it could not be read (" + ex.GetType().Name + ")";
                return false;
            }
        }

        /// <summary>Validates and, if good, stores the licence for future runs.</summary>
        public static bool Install(string licenceText, out string error)
        {
            Licence licence;
            if (!TryValidate(licenceText, out licence, out error)) return false;

            try
            {
                string path = StoredLicencePath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, licenceText.Trim());
            }
            catch (Exception ex)
            {
                error = "the licence is valid but could not be saved (" + ex.Message + ")";
                return false;
            }

            Refresh();
            return true;
        }

        // -------------------------------------------------------------- storage

        /// <summary>A gymclock.licence file beside the exe wins, so a USB copy can carry its own.</summary>
        private static string ReadStoredLicence()
        {
            try
            {
                string beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gymclock.licence");
                if (File.Exists(beside)) return File.ReadAllText(beside);
            }
            catch { }

            try
            {
                string stored = StoredLicencePath();
                if (File.Exists(stored)) return File.ReadAllText(stored);
            }
            catch { }

            return null;
        }

        private static string StoredLicencePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GymClock", "licence.txt");
        }

        // ------------------------------------------------------- clock rollback

        /// <summary>
        /// Records the most recent date the app has ever seen, in two places, so that
        /// deleting one of them is not enough to reset it.
        /// </summary>
        private static void RecordRun(DateTime today)
        {
            DateTime known = LastRunRecorded();
            if (today <= known) return;

            string value = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key != null) key.SetValue("ls", value);
                }
            }
            catch { }

            try
            {
                string path = StatePath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, value);
            }
            catch { }
        }

        private static DateTime LastRunRecorded()
        {
            DateTime latest = DateTime.MinValue;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key != null) latest = Max(latest, ParseDate(key.GetValue("ls") as string));
                }
            }
            catch { }

            try
            {
                string path = StatePath();
                if (File.Exists(path)) latest = Max(latest, ParseDate(File.ReadAllText(path)));
            }
            catch { }

            // A wildly future date means a broken clock, not a rollback attempt.
            if (latest > new DateTime(2060, 1, 1)) return DateTime.MinValue;

            return latest;
        }

        /// <summary>
        /// How many days until the app stops running. int.MaxValue for a perpetual
        /// licence, which is what stops perpetual users being nagged.
        /// </summary>
        public static int DaysUntilItStops(LicenceStatus status)
        {
            switch (status.State)
            {
                case LicenceState.Licensed:
                case LicenceState.TrialEnding:
                case LicenceState.LicenceGrace:
                    return status.DaysRemaining;

                case LicenceState.TrialExpired:
                case LicenceState.LicenceExpired:
                    return 0;

                default:
                    return int.MaxValue;
            }
        }

        /// <summary>
        /// True when the renewal form should be offered. Fires at 30 days out, which
        /// for the initial period means 1 April 2027, and thereafter means 30 days
        /// before each licence expires. Suppressed for a while once asked, so nobody
        /// gets nagged at the start of every lesson.
        /// </summary>
        public static bool ShouldPromptForRenewal(LicenceStatus status)
        {
            if (status.State == LicenceState.TrialActive) return false;
            if (status.State == LicenceState.NotConfigured) return false;
            if (status.State == LicenceState.ClockRolledBack) return false;

            if (DaysUntilItStops(status) > 30) return false;

            return DateTime.Now.Date >= NextRenewalPromptDue();
        }

        private static DateTime NextRenewalPromptDue()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    string raw = key == null ? null : key.GetValue("rp") as string;
                    DateTime value;
                    if (!string.IsNullOrEmpty(raw) && DateTime.TryParseExact(raw, "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
                    {
                        return value;
                    }
                }
            }
            catch { }

            return DateTime.MinValue;
        }

        /// <summary>Hold off on the renewal prompt for the given number of days.</summary>
        public static void RecordRenewalPrompt(int quietDays)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key != null)
                    {
                        key.SetValue("rp", DateTime.Now.Date.AddDays(quietDays)
                            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    }
                }
            }
            catch { }
        }

        /// <summary>When the app last looked online for a licence.</summary>
        public static DateTime LastOnlineCheck()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    string raw = key == null ? null : key.GetValue("lc") as string;
                    DateTime value;
                    if (!string.IsNullOrEmpty(raw) && DateTime.TryParseExact(raw, "yyyy-MM-dd HH:mm",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
                    {
                        return value;
                    }
                }
            }
            catch { }

            return DateTime.MinValue;
        }

        public static void RecordOnlineCheck()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key != null)
                    {
                        key.SetValue("lc", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                    }
                }
            }
            catch { }
        }

        private static string StatePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GymClock", "state.dat");
        }

        private static DateTime ParseDate(string text)
        {
            DateTime value;
            if (!string.IsNullOrEmpty(text) && DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                return value;
            }
            return DateTime.MinValue;
        }

        private static DateTime Max(DateTime a, DateTime b)
        {
            return a > b ? a : b;
        }

        // ------------------------------------------------------------ machine id

        private static string _machineId;

        /// <summary>
        /// A stable, non-identifying fingerprint shown to the user so you can issue a
        /// machine-locked key. Derived from the Windows crypto MachineGuid.
        /// </summary>
        public static string MachineId()
        {
            if (_machineId != null) return _machineId;

            string seed = null;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Cryptography", false))
                {
                    if (key != null) seed = key.GetValue("MachineGuid") as string;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(seed)) seed = Environment.MachineName;

            byte[] hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes("GymClock|" + seed));
            }

            string encoded = Base32(hash, 12);
            _machineId = encoded.Substring(0, 4) + "-" + encoded.Substring(4, 4) + "-" + encoded.Substring(8, 4);
            return _machineId;
        }

        /// <summary>Crockford-style alphabet: no I, O, 0 or 1 to confuse anyone reading it aloud.</summary>
        private static string Base32(byte[] data, int characters)
        {
            const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

            StringBuilder sb = new StringBuilder();
            int buffer = 0;
            int bits = 0;

            for (int i = 0; i < data.Length && sb.Length < characters; i++)
            {
                buffer = (buffer << 8) | data[i];
                bits += 8;

                while (bits >= 5 && sb.Length < characters)
                {
                    sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
            }

            while (sb.Length < characters) sb.Append(Alphabet[0]);
            return sb.ToString();
        }

        // ------------------------------------------------------------- encoding

        public static string ToBase64Url(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static byte[] FromBase64Url(string text)
        {
            string s = text.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        // ---------------------------------------------------------- description

        /// <summary>One-line summary for the corner of the clock display.</summary>
        public static string ShortDescription(LicenceStatus status)
        {
            switch (status.State)
            {
                case LicenceState.Licensed:
                    return status.Licence != null && status.Licence.Name.Length > 0
                        ? "Licensed to " + status.Licence.Name
                        : "Licensed";

                case LicenceState.LicenceGrace:
                    return string.Format(CultureInfo.CurrentCulture,
                        "{0} - LICENCE EXPIRED, renewal needed within {1} day{2}",
                        status.Licence != null ? status.Licence.Name : "Unlicensed",
                        status.DaysRemaining, status.DaysRemaining == 1 ? string.Empty : "s");

                case LicenceState.TrialEnding:
                    return string.Format(CultureInfo.CurrentCulture,
                        "UNLICENSED - {0} day{1} left (a licence key is required from {2})",
                        status.DaysRemaining, status.DaysRemaining == 1 ? string.Empty : "s",
                        FreeUseEndsOn.AddDays(1).ToString("d MMM yyyy", CultureInfo.CurrentCulture));

                case LicenceState.TrialActive:
                    // Deliberately silent until WarningStartsOn.
                    return string.Empty;

                default:
                    return "Licence required";
            }
        }
    }

    /// <summary>
    /// The requester's own name, school and email, remembered locally so a renewal
    /// does not mean typing them again. Never transmitted by the app - they only
    /// ever leave the machine inside an email the user sends by hand.
    /// </summary>
    public class RequesterDetails
    {
        public string Name = string.Empty;
        public string School = string.Empty;
        public string Email = string.Empty;

        private const string RegistryPath = @"Software\GymClock\Requester";

        public static RequesterDetails Load()
        {
            RequesterDetails details = new RequesterDetails();

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key != null)
                    {
                        details.Name = (key.GetValue("name") as string) ?? string.Empty;
                        details.School = (key.GetValue("school") as string) ?? string.Empty;
                        details.Email = (key.GetValue("email") as string) ?? string.Empty;
                    }
                }
            }
            catch { }

            return details;
        }

        public void Save()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key != null)
                    {
                        key.SetValue("name", Name ?? string.Empty);
                        key.SetValue("school", School ?? string.Empty);
                        key.SetValue("email", Email ?? string.Empty);
                    }
                }
            }
            catch { }
        }
    }
}
