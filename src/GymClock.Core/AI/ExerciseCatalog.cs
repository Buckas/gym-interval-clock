using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GymClock.AI
{
    /// <summary>One exercise as stored in the JSON files.</summary>
    public sealed class ExerciseRecord
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("cue")] public string Cue { get; set; } = string.Empty;
        [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new List<string>();
        [JsonPropertyName("equipment")] public List<string> Equipment { get; set; } = new List<string>();
        [JsonPropertyName("floor")] public bool Floor { get; set; }

        public ExerciseRecord Clone()
        {
            return new ExerciseRecord
            {
                Id = Id, Name = Name, Cue = Cue, Floor = Floor,
                Tags = new List<string>(Tags ?? new List<string>()),
                Equipment = new List<string>(Equipment ?? new List<string>())
            };
        }
    }

    /// <summary>The shared library file (built into the app, or a newer downloaded copy). App-owned.</summary>
    public sealed class LibraryDocument
    {
        [JsonPropertyName("format")] public int Format { get; set; } = 1;
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("updated")] public string Updated { get; set; } = string.Empty;
        [JsonPropertyName("exercises")] public List<ExerciseRecord> Exercises { get; set; } = new List<ExerciseRecord>();
    }

    /// <summary>
    /// The teacher's own file. Never written by library updates. An exercise here with the same id
    /// as a library exercise is an edit (it wins); any other id is the teacher's own exercise.
    /// </summary>
    public sealed class UserExerciseDocument
    {
        [JsonPropertyName("format")] public int Format { get; set; } = 1;

        /// <summary>Equipment the gym has; null = not set (suggest anything).</summary>
        [JsonPropertyName("equipment")] public List<string> Equipment { get; set; }

        [JsonPropertyName("hidden")] public List<string> Hidden { get; set; } = new List<string>();
        [JsonPropertyName("exercises")] public List<ExerciseRecord> Exercises { get; set; } = new List<ExerciseRecord>();

        /// <summary>Equipment the teacher has added, on top of the built-in list.</summary>
        [JsonPropertyName("customEquipment")] public List<CustomEquipment> CustomEquipment { get; set; } = new List<CustomEquipment>();
    }

    /// <summary>Equipment the teacher has added (e.g. "Sled", "Agility ladder").</summary>
    public sealed class CustomEquipment
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }

    public enum ExerciseOrigin { Library, Edited, Custom }

    public sealed class CatalogEntry
    {
        public string Id { get; set; }
        public ExerciseRecord Record { get; set; }
        public Exercise Exercise { get; set; }
        public ExerciseOrigin Origin { get; set; }
        public bool Hidden { get; set; }
    }

    /// <summary>The merged view the app uses: library + the teacher's edits, own exercises, hidden list and equipment.</summary>
    public sealed class CatalogSnapshot
    {
        public int LibraryVersion { get; set; }
        public bool LibraryIsDownloaded { get; set; }
        public string LibraryUpdated { get; set; } = string.Empty;
        public List<CatalogEntry> Entries { get; set; } = new List<CatalogEntry>();

        /// <summary>Equipment the gym has (null = not set, everything allowed).</summary>
        public ExerciseKit? OwnedKit { get; set; }

        /// <summary>Teacher-added equipment the gym has (ids); only meaningful when OwnedKit is set.</summary>
        public HashSet<string> OwnedOther { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Equipment the teacher has added (for the Exercises window and for reading requests).</summary>
        public List<CustomEquipment> CustomEquipment { get; set; } = new List<CustomEquipment>();

        public IReadOnlyList<Exercise> Visible
        {
            get { return Entries.Where(e => !e.Hidden).Select(e => e.Exercise).ToList(); }
        }
    }

    public enum LibraryUpdateResult { Updated, AlreadyUpToDate, Unavailable, Invalid }

    /// <summary>
    /// Loads and layers the exercise data:
    ///   1. the library  - built into the exe, or the downloaded copy when it has a higher version;
    ///   2. the teacher's file (my-exercises.json) on top - edits win, own exercises are added,
    ///      hidden ones are removed, and the gym's equipment list filters suggestions.
    /// Library downloads only ever replace the app-owned library copy, so the teacher's file -
    /// and therefore every edit they made - is never touched; unedited library exercises update
    /// and new ones appear.
    /// </summary>
    public static class ExerciseCatalog
    {
        /// <summary>Where newer libraries are published (the app's GitHub repository).</summary>
        public const string LibraryUrl = "https://raw.githubusercontent.com/Buckas/gym-interval-clock/main/data/exercises.json";

        private const int MinimumLibrarySize = 20;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static readonly object Sync = new object();
        private static CatalogSnapshot _current;
        private static string _folderOverride;

        /// <summary>%AppData%\GymClock\Exercises</summary>
        public static string Folder
        {
            get
            {
                return _folderOverride ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GymClock", "Exercises");
            }
        }

        public static string UserFilePath { get { return Path.Combine(Folder, "my-exercises.json"); } }
        public static string DownloadedLibraryPath { get { return Path.Combine(Folder, "library.json"); } }
        private static string LastCheckPath { get { return Path.Combine(Folder, "library-check.txt"); } }

        /// <summary>Tests point the catalog at a temporary folder.</summary>
        public static void UseFolderForTesting(string folder)
        {
            lock (Sync) { _folderOverride = folder; _current = null; }
        }

        public static event EventHandler Changed;

        /// <summary>The merged catalog (loaded on first use, cached until something changes).</summary>
        public static CatalogSnapshot Current
        {
            get
            {
                lock (Sync)
                {
                    if (_current == null) _current = LoadFromDisk();
                    return _current;
                }
            }
        }

        public static void Reload()
        {
            lock (Sync) { _current = null; }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        // ------------------------------------------------------------------ loading

        public static LibraryDocument LoadBuiltInLibrary()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GymClock.exercises.json"))
            {
                if (stream == null) return new LibraryDocument();
                using (StreamReader reader = new StreamReader(stream))
                {
                    return ParseLibrary(reader.ReadToEnd()) ?? new LibraryDocument();
                }
            }
        }

        public static LibraryDocument ParseLibrary(string json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<LibraryDocument>(json, Json); }
            catch { return null; }
        }

        public static UserExerciseDocument LoadUserDocument()
        {
            try
            {
                if (File.Exists(UserFilePath))
                {
                    return JsonSerializer.Deserialize<UserExerciseDocument>(File.ReadAllText(UserFilePath), Json) ?? new UserExerciseDocument();
                }
            }
            catch { }
            return new UserExerciseDocument();
        }

        private static CatalogSnapshot LoadFromDisk()
        {
            LibraryDocument builtIn = LoadBuiltInLibrary();
            LibraryDocument downloaded = null;
            try { if (File.Exists(DownloadedLibraryPath)) downloaded = ParseLibrary(File.ReadAllText(DownloadedLibraryPath)); } catch { }
            return Merge(builtIn, downloaded, LoadUserDocument());
        }

        /// <summary>The layering rules, kept pure so they can be tested directly.</summary>
        public static CatalogSnapshot Merge(LibraryDocument builtIn, LibraryDocument downloaded, UserExerciseDocument user)
        {
            bool useDownloaded = downloaded != null && IsUsable(downloaded) && downloaded.Version > (builtIn?.Version ?? 0);
            LibraryDocument library = useDownloaded ? downloaded : (builtIn ?? new LibraryDocument());
            user = user ?? new UserExerciseDocument();

            Dictionary<string, ExerciseRecord> mine = new Dictionary<string, ExerciseRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (ExerciseRecord r in user.Exercises ?? new List<ExerciseRecord>())
            {
                if (!string.IsNullOrWhiteSpace(r?.Id)) mine[r.Id] = r;
            }
            HashSet<string> hidden = new HashSet<string>(user.Hidden ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            CatalogSnapshot snapshot = new CatalogSnapshot
            {
                LibraryVersion = library.Version,
                LibraryIsDownloaded = useDownloaded,
                LibraryUpdated = library.Updated ?? string.Empty,
                OwnedKit = user.Equipment == null ? (ExerciseKit?)null : ParseKit(user.Equipment),
                OwnedOther = new HashSet<string>(OtherEquipment(user.Equipment), StringComparer.OrdinalIgnoreCase),
                CustomEquipment = (user.CustomEquipment ?? new List<CustomEquipment>())
                    .Where(c => !string.IsNullOrWhiteSpace(c?.Id) && !string.IsNullOrWhiteSpace(c.Name)).ToList()
            };

            HashSet<string> libraryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ExerciseRecord r in library.Exercises ?? new List<ExerciseRecord>())
            {
                if (string.IsNullOrWhiteSpace(r?.Id) || string.IsNullOrWhiteSpace(r.Name) || !libraryIds.Add(r.Id)) continue;
                bool edited = mine.TryGetValue(r.Id, out ExerciseRecord edit);
                ExerciseRecord effective = edited ? edit : r;
                snapshot.Entries.Add(new CatalogEntry
                {
                    Id = r.Id,
                    Record = effective,
                    Exercise = ToExercise(effective),
                    Origin = edited ? ExerciseOrigin.Edited : ExerciseOrigin.Library,
                    Hidden = hidden.Contains(r.Id)
                });
            }

            foreach (ExerciseRecord r in mine.Values)
            {
                if (libraryIds.Contains(r.Id) || string.IsNullOrWhiteSpace(r.Name)) continue;
                snapshot.Entries.Add(new CatalogEntry
                {
                    Id = r.Id,
                    Record = r,
                    Exercise = ToExercise(r),
                    Origin = ExerciseOrigin.Custom,
                    Hidden = hidden.Contains(r.Id)
                });
            }

            return snapshot;
        }

        private static bool IsUsable(LibraryDocument doc)
        {
            if (doc == null || doc.Format != 1 || doc.Version <= 0 || doc.Exercises == null) return false;
            List<ExerciseRecord> valid = doc.Exercises.Where(e => !string.IsNullOrWhiteSpace(e?.Id) && !string.IsNullOrWhiteSpace(e.Name)).ToList();
            return valid.Count >= MinimumLibrarySize
                && valid.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == valid.Count;
        }

        // ------------------------------------------------------------------ teacher's changes

        /// <summary>Saves the teacher's file (atomically) and reloads.</summary>
        public static void SaveUserDocument(UserExerciseDocument user)
        {
            Directory.CreateDirectory(Folder);
            WriteAtomically(UserFilePath, JsonSerializer.Serialize(user, Json));
            Reload();
        }

        /// <summary>An id for teacher-added equipment, e.g. "Agility ladder" -> "my-agility-ladder".</summary>
        public static string NewEquipmentId(string name)
        {
            string slug = System.Text.RegularExpressions.Regex.Replace((name ?? "equipment").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return "my-" + (slug.Length == 0 ? "equipment" : slug);
        }

        public static string NewCustomId(string name)
        {
            string slug = System.Text.RegularExpressions.Regex.Replace((name ?? "exercise").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return "my-" + (slug.Length == 0 ? "exercise" : slug) + "-" + DateTime.Now.ToString("yyyyMMddHHmmss");
        }

        // ------------------------------------------------------------------ library updates

        /// <summary>
        /// Installs a downloaded library if it is valid and newer than the one in use. Only the
        /// app-owned library copy is written; the teacher's file is never touched.
        /// </summary>
        public static LibraryUpdateResult ApplyDownloadedLibrary(string json)
        {
            LibraryDocument doc = ParseLibrary(json);
            if (doc == null || !IsUsable(doc)) return LibraryUpdateResult.Invalid;
            if (doc.Version <= Current.LibraryVersion) return LibraryUpdateResult.AlreadyUpToDate;

            Directory.CreateDirectory(Folder);
            WriteAtomically(DownloadedLibraryPath, json);
            Reload();
            return LibraryUpdateResult.Updated;
        }

        /// <summary>Downloads the published library and installs it if newer. Fails quietly when offline.</summary>
        public static async Task<LibraryUpdateResult> CheckForUpdateAsync(bool force, CancellationToken cancellationToken = default)
        {
            if (!force && !CheckIsDue()) return LibraryUpdateResult.AlreadyUpToDate;

            string json;
            try
            {
                using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "GymClock/" + typeof(ExerciseCatalog).Assembly.GetName().Version);
                    using (HttpResponseMessage response = await client.GetAsync(LibraryUrl, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) return LibraryUpdateResult.Unavailable;
                        json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return LibraryUpdateResult.Unavailable; }

            RecordCheck();
            return ApplyDownloadedLibrary(json);
        }

        private static bool CheckIsDue()
        {
            try
            {
                if (!File.Exists(LastCheckPath)) return true;
                return DateTime.TryParse(File.ReadAllText(LastCheckPath), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTime last)
                    ? DateTime.UtcNow - last > CheckInterval
                    : true;
            }
            catch { return true; }
        }

        private static void RecordCheck()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(LastCheckPath, DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch { }
        }

        private static void WriteAtomically(string path, string content)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, content);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        // ------------------------------------------------------------------ conversions

        private static readonly Dictionary<string, ExerciseTag> TagNames = Enum.GetValues(typeof(ExerciseTag)).Cast<ExerciseTag>()
            .Where(t => t != ExerciseTag.None).ToDictionary(t => t.ToString().ToLowerInvariant(), t => t);

        private static readonly Dictionary<string, ExerciseKit> KitNames = Enum.GetValues(typeof(ExerciseKit)).Cast<ExerciseKit>()
            .Where(k => k != ExerciseKit.None).ToDictionary(k => k.ToString().ToLowerInvariant(), k => k);

        public static Exercise ToExercise(ExerciseRecord r)
        {
            ExerciseTag tags = ExerciseTag.None;
            foreach (string t in r.Tags ?? new List<string>())
            {
                if (TagNames.TryGetValue((t ?? string.Empty).Trim().ToLowerInvariant(), out ExerciseTag tag)) tags |= tag;
            }
            return new Exercise(r.Name, r.Cue ?? string.Empty, tags, ParseKit(r.Equipment), r.Floor, r.Id, OtherEquipment(r.Equipment));
        }

        /// <summary>Equipment names that aren't built in - i.e. teacher-added equipment ids.</summary>
        public static List<string> OtherEquipment(IEnumerable<string> names)
        {
            return (names ?? Enumerable.Empty<string>())
                .Select(n => (n ?? string.Empty).Trim().ToLowerInvariant())
                .Where(n => n.Length > 0 && !KitNames.ContainsKey(n))
                .Distinct()
                .ToList();
        }

        public static ExerciseKit ParseKit(IEnumerable<string> names)
        {
            ExerciseKit kit = ExerciseKit.None;
            foreach (string k in names ?? Enumerable.Empty<string>())
            {
                if (KitNames.TryGetValue((k ?? string.Empty).Trim().ToLowerInvariant(), out ExerciseKit value)) kit |= value;
            }
            return kit;
        }

        public static List<string> KitToNames(ExerciseKit kit)
        {
            return KitNames.Where(p => (kit & p.Value) != 0).Select(p => p.Key).ToList();
        }

        public static List<string> TagsToNames(ExerciseTag tags)
        {
            return TagNames.Where(p => (tags & p.Value) != 0).Select(p => p.Key).ToList();
        }
    }
}
