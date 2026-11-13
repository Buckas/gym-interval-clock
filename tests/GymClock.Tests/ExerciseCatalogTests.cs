using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GymClock.AI;
using Xunit;

namespace GymClock.Tests
{
    /// <summary>Shares the static catalog, so these never run in parallel with each other.</summary>
    [CollectionDefinition("ExerciseCatalog", DisableParallelization = true)]
    public class ExerciseCatalogCollection { }

    [Collection("ExerciseCatalog")]
    public class ExerciseCatalogTests : IDisposable
    {
        private readonly string _folder;

        public ExerciseCatalogTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), "gymclock-catalog-" + Guid.NewGuid().ToString("N"));
            ExerciseCatalog.UseFolderForTesting(_folder);
        }

        public void Dispose()
        {
            ExerciseCatalog.UseFolderForTesting(null);
            try { Directory.Delete(_folder, true); } catch { }
        }

        private static ExerciseRecord Rec(string id, string name, string cue = "cue", params string[] tags) =>
            new ExerciseRecord { Id = id, Name = name, Cue = cue, Tags = tags.ToList() };

        /// <summary>A valid library (needs 20+ exercises) with a few named ones first.</summary>
        private static LibraryDocument Library(int version, params ExerciseRecord[] named)
        {
            LibraryDocument doc = new LibraryDocument { Version = version, Updated = "2026-11-" + (10 + version) };
            doc.Exercises.AddRange(named);
            for (int i = 0; doc.Exercises.Count < 25; i++) doc.Exercises.Add(Rec("filler-" + i, "Filler " + i, "cue", "cardio"));
            return doc;
        }

        [Fact]
        public void BuiltInLibrary_IsBundledInTheApp()
        {
            LibraryDocument builtIn = ExerciseCatalog.LoadBuiltInLibrary();
            Assert.True(builtIn.Version >= 1);
            Assert.True(builtIn.Exercises.Count >= 100);
            Assert.Contains(builtIn.Exercises, e => e.Id == "push-ups" && e.Floor);
        }

        [Fact]
        public void NewerDownload_ReplacesLibrary_OlderOrBrokenOnesAreIgnored()
        {
            LibraryDocument builtIn = Library(1, Rec("push-ups", "Push Ups"));

            Assert.True(ExerciseCatalog.Merge(builtIn, Library(2, Rec("push-ups", "Push Ups")), null).LibraryIsDownloaded);
            Assert.False(ExerciseCatalog.Merge(builtIn, Library(1, Rec("push-ups", "Push Ups")), null).LibraryIsDownloaded);

            LibraryDocument tooSmall = new LibraryDocument { Version = 9 };
            tooSmall.Exercises.Add(Rec("x", "X"));
            Assert.False(ExerciseCatalog.Merge(builtIn, tooSmall, null).LibraryIsDownloaded);
        }

        [Fact]
        public void Update_ChangesOnlyUneditedExercises_AddsNewOnes_KeepsTeachersChanges()
        {
            LibraryDocument v1 = Library(1, Rec("push-ups", "Push Ups", "old cue"), Rec("plank", "Plank", "old cue"), Rec("burpees", "Burpees"));
            LibraryDocument v2 = Library(2, Rec("push-ups", "Push Ups", "NEW cue"), Rec("plank", "Plank", "NEW cue"),
                Rec("burpees", "Burpees"), Rec("bear-crawl", "Bear Crawl", "brand new"));

            UserExerciseDocument user = new UserExerciseDocument();
            user.Exercises.Add(Rec("plank", "Plank (our way)", "teacher's cue"));        // edited library exercise
            user.Exercises.Add(Rec("my-sled-push", "Sled Push", "drive low"));          // teacher's own
            user.Hidden.Add("burpees");

            CatalogSnapshot s = ExerciseCatalog.Merge(v1, v2, user);
            CatalogEntry Get(string id) => s.Entries.Single(e => e.Id == id);

            Assert.Equal("NEW cue", Get("push-ups").Record.Cue);                       // unedited -> updated
            Assert.Equal(ExerciseOrigin.Library, Get("push-ups").Origin);
            Assert.Equal("teacher's cue", Get("plank").Record.Cue);                    // edited -> teacher wins
            Assert.Equal(ExerciseOrigin.Edited, Get("plank").Origin);
            Assert.Equal("Bear Crawl", Get("bear-crawl").Record.Name);                  // new -> added
            Assert.Equal(ExerciseOrigin.Custom, Get("my-sled-push").Origin);           // own -> kept
            Assert.True(Get("burpees").Hidden);                                       // hidden -> stays hidden
            Assert.DoesNotContain(s.Visible, e => e.Name == "Burpees");
        }

        [Fact]
        public void EditedExercise_RemovedFromLibrary_IsKeptAsTheTeachersOwn()
        {
            UserExerciseDocument user = new UserExerciseDocument();
            user.Exercises.Add(Rec("old-move", "Old Move (edited)"));
            CatalogSnapshot s = ExerciseCatalog.Merge(Library(1, Rec("old-move", "Old Move")), Library(2), user);

            CatalogEntry kept = s.Entries.Single(e => e.Id == "old-move");
            Assert.Equal(ExerciseOrigin.Custom, kept.Origin);
            Assert.Equal("Old Move (edited)", kept.Record.Name);
        }

        [Fact]
        public void ApplyingADownload_NeverTouchesTheTeachersFile()
        {
            UserExerciseDocument user = new UserExerciseDocument { Equipment = new List<string> { "dumbbell", "my-sled" } };
            user.Exercises.Add(Rec("push-ups", "Push Ups (knees ok)", "teacher's cue", "strength"));
            user.Hidden.Add("burpees");
            user.CustomEquipment.Add(new CustomEquipment { Id = "my-sled", Name = "Sled" });
            ExerciseCatalog.SaveUserDocument(user);
            byte[] before = File.ReadAllBytes(ExerciseCatalog.UserFilePath);

            int current = ExerciseCatalog.Current.LibraryVersion;
            string newer = JsonSerializer.Serialize(Library(current + 1, Rec("push-ups", "Push Ups", "library cue"), Rec("new-one", "New One")));

            Assert.Equal(LibraryUpdateResult.Updated, ExerciseCatalog.ApplyDownloadedLibrary(newer));
            Assert.Equal(before, File.ReadAllBytes(ExerciseCatalog.UserFilePath));
            Assert.True(File.Exists(ExerciseCatalog.DownloadedLibraryPath));

            CatalogSnapshot s = ExerciseCatalog.Current;
            Assert.Equal(current + 1, s.LibraryVersion);
            Assert.Equal("teacher's cue", s.Entries.Single(e => e.Id == "push-ups").Record.Cue);
            Assert.Contains(s.Entries, e => e.Id == "new-one");

            // Same version again, or rubbish, changes nothing.
            Assert.Equal(LibraryUpdateResult.AlreadyUpToDate, ExerciseCatalog.ApplyDownloadedLibrary(newer));
            Assert.Equal(LibraryUpdateResult.Invalid, ExerciseCatalog.ApplyDownloadedLibrary("<html>404</html>"));
            Assert.Equal(before, File.ReadAllBytes(ExerciseCatalog.UserFilePath));
        }

        private static void SaveSledGym(bool gymHasSled)
        {
            UserExerciseDocument user = new UserExerciseDocument
            {
                Equipment = gymHasSled ? new List<string> { "dumbbell", "my-sled" } : new List<string> { "dumbbell" }
            };
            user.CustomEquipment.Add(new CustomEquipment { Id = "my-sled", Name = "Sled" });
            ExerciseRecord push = Rec("my-sled-push", "Sled Push", "Drive low through the legs", "strength", "lower", "cardio");
            push.Equipment.Add("my-sled");
            user.Exercises.Add(push);
            ExerciseRecord pull = Rec("my-sled-pull", "Sled Rope Pull", "Hand over hand, stay low", "strength", "upper");
            pull.Equipment.Add("my-sled");
            user.Exercises.Add(pull);
            ExerciseCatalog.SaveUserDocument(user);
        }

        [Fact]
        public void AddedEquipment_IsUnderstoodInRequests()
        {
            SaveSledGym(gymHasSled: true);

            ExerciseQuery q = ExerciseLibrary.Parse("give me 2 sled exercises");
            Assert.Contains("my-sled", q.AnyOfOtherKit);
            var picks = ExerciseLibrary.Pick(q, null, new Random(1), out _);
            Assert.Equal(new[] { "Sled Pull", "Sled Push" }.Length, picks.Count);
            Assert.All(picks, e => Assert.Contains("my-sled", e.OtherKit));

            string reply = ExerciseLibrary.Answer("2 exercises using sleds", new ConversationState(), new Random(2));
            Assert.Contains("using a sled", reply);
            Assert.Contains("Sled", reply);
        }

        [Fact]
        public void AddedEquipment_FollowsTheGymFilter_AndNoEquipmentRequests()
        {
            SaveSledGym(gymHasSled: false);
            var strength = ExerciseLibrary.Pick(ExerciseLibrary.Parse("give me 12 resistance exercises"), null, new Random(3), out _);
            Assert.DoesNotContain(strength, e => e.OtherKit.Count > 0);                         // gym hasn't got one

            Assert.NotEmpty(ExerciseLibrary.Pick(ExerciseLibrary.Parse("2 sled exercises"), null, new Random(3), out _)
                .Where(e => e.OtherKit.Contains("my-sled")));                                    // ...unless asked for by name

            SaveSledGym(gymHasSled: true);
            var bodyweight = ExerciseLibrary.Pick(ExerciseLibrary.Parse("12 strength exercises with no equipment"), null, new Random(4), out _);
            Assert.DoesNotContain(bodyweight, e => e.NeedsEquipment);
        }

        [Fact]
        public void GymEquipment_FiltersSuggestions_UnlessAskedForByName()
        {
            UserExerciseDocument user = new UserExerciseDocument { Equipment = new List<string> { "dumbbell" } };
            ExerciseCatalog.SaveUserDocument(user);

            ExerciseQuery anyStrength = ExerciseLibrary.Parse("give me 12 resistance exercises");
            var picks = ExerciseLibrary.Pick(anyStrength, null, new Random(1), out _);
            Assert.All(picks, e => Assert.True((e.Kit & ~(ExerciseKit.Dumbbell | ExerciseKit.Wall)) == ExerciseKit.None
                || (e.Kit & ExerciseKit.Dumbbell) != 0, e.Name + " needs " + e.Kit));

            ExerciseQuery ropes = ExerciseLibrary.Parse("3 battle rope exercises");
            Assert.Contains(ExerciseLibrary.Pick(ropes, null, new Random(1), out _), e => (e.Kit & ExerciseKit.BattleRope) != 0);
        }
    }
}
