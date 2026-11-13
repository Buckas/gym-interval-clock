using System;
using System.Linq;
using System.Threading.Tasks;
using GymClock.AI;
using Xunit;

namespace GymClock.Tests
{
    [Collection("ExerciseCatalog")]
    public class ExerciseLibraryTests : IDisposable
    {
        private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gymclock-lib-" + Guid.NewGuid().ToString("N"));

        // Use only the built-in library - never the teacher's real AppData files.
        public ExerciseLibraryTests() { ExerciseCatalog.UseFolderForTesting(_folder); }

        public void Dispose()
        {
            ExerciseCatalog.UseFolderForTesting(null);
            try { System.IO.Directory.Delete(_folder, true); } catch { }
        }

        [Theory]
        [InlineData("I need 5 examples of resistance exercises to use with basic weights", true)]
        [InlineData("I need 4 floor based exercises to do without any equipment", true)]
        [InlineData("Give me some core exercises", true)]
        [InlineData("any ideas for low impact cardio moves?", true)]
        [InlineData("Replace Burpees with Tuck Jumps", false)]
        [InlineData("Help me create a new program", false)]
        [InlineData("How many reps for box jumps?", false)]
        public void Intent(string request, bool expected)
        {
            Assert.Equal(expected, ExerciseLibrary.IsIdeasRequest(request));
        }

        [Fact]
        public void FiveResistanceExercises_WithBasicWeights()
        {
            ExerciseQuery q = ExerciseLibrary.Parse("I need 5 examples of resistance exercises to use with basic weights");
            var picks = ExerciseLibrary.Pick(q, null, new Random(1), out bool relaxed);

            Assert.Equal(5, picks.Count);
            Assert.False(relaxed);
            Assert.All(picks, e =>
            {
                Assert.True((e.Tags & ExerciseTag.Strength) != 0, e.Name);
                Assert.True((e.Kit & (ExerciseKit.Dumbbell | ExerciseKit.Kettlebell | ExerciseKit.WeightPlate)) != 0, e.Name);
            });
        }

        [Fact]
        public void FourFloorExercises_NoEquipment()
        {
            ExerciseQuery q = ExerciseLibrary.Parse("I need 4 floor based exercises to do without any equipment");
            Assert.Equal(4, q.Count);
            Assert.True(q.NoEquipment);
            Assert.True(q.FloorOnly);

            var picks = ExerciseLibrary.Pick(q, null, new Random(2), out _);
            Assert.Equal(4, picks.Count);
            Assert.All(picks, e => { Assert.True(e.Floor, e.Name); Assert.Equal(ExerciseKit.None, e.Kit); });
        }

        [Theory]
        [InlineData("five core exercises for year 7s", 5)]
        [InlineData("I need 3 upper body ideas", 3)]
        [InlineData("give me some cardio exercises", 5)]
        [InlineData("20 exercises please", 12)]
        public void Count_IsTheNumberThatGoesWithTheExercises(string request, int expected)
        {
            Assert.Equal(expected, ExerciseLibrary.Parse(request).Count);
        }

        [Fact]
        public void More_GivesDifferentOnes_AndAnswerRemembersThem()
        {
            ConversationState state = new ConversationState();
            ExerciseLibrary.Answer("give me 4 cardio exercises", state, new Random(3));
            var first = state.LastExerciseIdeas.ToList();
            Assert.Equal(4, first.Count);

            ExerciseLibrary.Answer("4 more cardio exercises", state, new Random(4));
            Assert.Empty(first.Intersect(state.LastExerciseIdeas));
        }

        [Fact]
        public void Answer_ListsNamesAndCues_AndChangesNothing()
        {
            string reply = ExerciseLibrary.Answer("I need 4 floor based exercises to do without any equipment", new ConversationState(), new Random(5));
            Assert.StartsWith("Here are 4 floor-based exercises with no equipment:", reply);
            Assert.Contains("1) ", reply);
            Assert.Contains("4) ", reply);
            Assert.Contains("Nothing has been changed", reply);
        }

        [Fact]
        public void Library_IsLargeAndHasNoDuplicates()
        {
            Assert.True(ExerciseLibrary.All.Count >= 100, "library size " + ExerciseLibrary.All.Count);
            Assert.Equal(ExerciseLibrary.All.Count, ExerciseLibrary.All.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(ExerciseLibrary.All, e => Assert.False(string.IsNullOrWhiteSpace(e.Cue), e.Name));
        }

        [Fact]
        public async Task UseThese_TurnsSuggestionsIntoStations()
        {
            ConversationState state = new ConversationState();
            ExerciseLibrary.Answer("I need 4 floor based exercises to do without any equipment", state, new Random(6));

            GuidedProgramBuilder b = new GuidedProgramBuilder("create a new program", null, state.LastExerciseIdeas);
            b.Start();
            string stationsQuestion = await b.AnswerAsync("4");
            Assert.Contains("USE THESE", stationsQuestion);
            await b.AnswerAsync("use these");

            Assert.Equal(state.LastExerciseIdeas, b.BuildProgram().Stations.Select(s => s.Name));
        }
    }
}
