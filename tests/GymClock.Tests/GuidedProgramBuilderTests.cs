using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GymClock.AI;
using Xunit;

namespace GymClock.Tests
{
    [Collection("ExerciseCatalog")]
    public class GuidedProgramBuilderTests : IDisposable
    {
        private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gymclock-builder-" + Guid.NewGuid().ToString("N"));

        // Coaching tips come from the exercise library - use the built-in one, never the teacher's AppData.
        public GuidedProgramBuilderTests() { ExerciseCatalog.UseFolderForTesting(_folder); }

        public void Dispose()
        {
            ExerciseCatalog.UseFolderForTesting(null);
            try { System.IO.Directory.Delete(_folder, true); } catch { }
        }

        private static List<(BlockType Type, int Seconds)> Flat(Timeline t) =>
            t.Resolve().Select(b => (b.Block.Type, b.Block.Seconds)).ToList();

        private static async Task<WorkoutProgram> RunAsync(GuidedProgramBuilder builder, params string[] answers)
        {
            builder.Start();
            foreach (string answer in answers) await builder.AnswerAsync(answer);
            Assert.Equal(GuidedProgramBuilder.Step.Done, builder.CurrentStep);

            ProgramValidationResult result = new ProgramProposalValidator().Validate(builder.Script);
            Assert.True(result.IsValid, string.Join(" | ", result.Issues.Select(i => i.Message)) + "\n" + builder.Script);
            return result.ParsedProgram;
        }

        private static int RunSeconds(WorkoutProgram p) => p.Mode == ExecutionMode.Parallel
            ? p.GetParallelRotationPlan().TotalSeconds
            : p.BuildRuntimeSequence().Sum(b => b.Block.Seconds);

        // ---------------------------------------------------------------- timing notation

        [Theory]
        [InlineData("40/20 x 8", 8, 40, 20, BlockType.Rest)]
        [InlineData("40 sec on / 20 sec off x 8", 8, 40, 20, BlockType.Rest)]
        [InlineData("1 min on / 30 sec off x 3", 3, 60, 30, BlockType.Rest)]
        [InlineData("1 min run @ 80% / 1 min recovery walk × 4", 4, 60, 60, BlockType.Recovery)]
        [InlineData("1:30 / 30 x2", 2, 90, 30, BlockType.Rest)]
        [InlineData("45/15, 6 rounds", 6, 45, 15, BlockType.Rest)]
        public void Pattern_WorkRestRounds(string text, int rounds, int work, int rest, BlockType restType)
        {
            Assert.True(GuidedProgramBuilder.TryParsePattern(text, out Timeline t, out _, out _));
            var blocks = Flat(t);
            Assert.Equal(rounds * 2, blocks.Count);
            Assert.Equal((BlockType.Work, work), blocks[0]);
            Assert.Equal((restType, rest), blocks[1]);
        }

        [Fact]
        public void Pattern_Tabata_Is8x20over10()
        {
            Assert.True(GuidedProgramBuilder.TryParsePattern("tabata", out Timeline t, out _, out _));
            Assert.Equal(240, t.TotalSeconds());
            Assert.Equal(16, Flat(t).Count);
        }

        [Fact]
        public void Pattern_Pyramid_GoesUpAndBackDown()
        {
            Assert.True(GuidedProgramBuilder.TryParsePattern("pyramid 20-30-40 / 20 x 2", out Timeline t, out _, out _));
            int[] works = Flat(t).Where(b => b.Type == BlockType.Work).Select(b => b.Seconds).ToArray();
            Assert.Equal(new[] { 20, 30, 40, 30, 20, 20, 30, 40, 30, 20 }, works);
            Assert.Equal(480, t.TotalSeconds()); // 8:00 per station, as on the whiteboard
        }

        [Fact]
        public void Pattern_WordsBecomeCoachingNotes()
        {
            Assert.True(GuidedProgramBuilder.TryParsePattern("40 sec high intensity / 20 sec spin recovery x 8", out _, out string work, out string rest));
            Assert.Equal("High intensity", work);
            Assert.Equal("Spin recovery", rest);
        }

        [Theory]
        [InlineData("1 min run @ 80% / 1 min recovery walk x 4", "Run at 80%", "Recovery walk")]
        [InlineData("40 sec of selected exercise / 20 sec rest x 8", "Selected exercise", null)]
        [InlineData("40 sec on / 20 sec off x 8", null, null)]
        [InlineData("40 sec continuous / 20 sec rope down x 8", "Continuous", "Rope down")]
        public void Pattern_NotesKeepTheTeachersWords(string text, string work, string rest)
        {
            Assert.True(GuidedProgramBuilder.TryParsePattern(text, out _, out string w, out string r));
            Assert.Equal(work, w);
            Assert.Equal(rest, r);
        }

        [Theory]
        [InlineData("Bike station → 40 sec high intensity / 20 sec spin recovery x 8", "Bike", "40 sec high intensity / 20 sec spin recovery x 8")]
        [InlineData("TREADMILL: 1 min run / 1 min walk x 4", "Treadmill", "1 min run / 1 min walk x 4")]
        [InlineData("3. Skipping Rope", "Skipping Rope", "")]
        [InlineData("Wall calf raises (1 min on / 30 sec off)", "Wall calf raises", "1 min on / 30 sec off")]
        public void StationLines_SplitIntoNameAndTiming(string line, string name, string timing)
        {
            GuidedProgramBuilder.SplitNameAndTiming(line, out string n, out string t);
            Assert.Equal(name, n);
            Assert.Equal(timing, t);
        }

        // ---------------------------------------------------------------- the four modes, end to end

        [Fact]
        public async Task Parallel_PastedWhiteboard_BuildsEverestSession1()
        {
            WorkoutProgram p = await RunAsync(new GuidedProgramBuilder("Help me create a new program"),
                "3",
                "Bike station → 40 sec high intensity / 20 sec spin recovery x 8\n"
                + "Treadmill station → 1 min run @ 80% / 1 min recovery walk x 4\n"
                + "Skipping station → 40 sec continuous / 20 sec rope down x 8\n"
                + "Floor station → 40 sec of selected exercise / 20 sec rest x 8",
                "1 min",
                "Everest Session 1",
                "y");

            Assert.Equal(ExecutionMode.Parallel, p.Mode);
            Assert.Equal("Everest Session 1", p.Name);
            Assert.Equal(new[] { "Bike", "Treadmill", "Skipping", "Floor" }, p.Stations.Select(s => s.Name));
            Assert.Equal("High intensity", p.Stations[0].WorkInstruction);
            Assert.Equal(35 * 60, RunSeconds(p));
        }

        [Fact]
        public async Task Circuit_OneClock_BuildsEverestSession3()
        {
            WorkoutProgram p = await RunAsync(new GuidedProgramBuilder("make a new circuit session"),
                "4",
                "Bike + Elliptical, Treadmill, Skipping Rope, Battle Rope / Plyo Box",
                "pyramid 20-30-40 / 20 x 2",
                "1 min",
                "Everest Session 3",
                "yes");

            Assert.Equal(ExecutionMode.Circuit, p.Mode);
            Assert.Equal(4, p.Stations.Count);
            Assert.Equal(35 * 60, RunSeconds(p));
        }

        [Fact]
        public async Task Shared_FixedStations_NoMoveQuestion()
        {
            GuidedProgramBuilder b = new GuidedProgramBuilder("create a new workout");
            b.Start();
            await b.AnswerAsync("1");
            await b.AnswerAsync("Squats, Push-Ups, Sit-Ups, Lunges");
            string next = await b.AnswerAsync("tabata");
            Assert.Equal(GuidedProgramBuilder.Step.Name, b.CurrentStep); // no move question for fixed stations
            Assert.Contains("called", next);
            await b.AnswerAsync("Team Tabata");
            await b.AnswerAsync("y");

            WorkoutProgram p = new ProgramProposalValidator().Validate(b.Script).ParsedProgram;
            Assert.Equal(ExecutionMode.Shared, p.Mode);
            Assert.Equal(4 * 60, RunSeconds(p));
            Assert.Equal(4, p.Stations.Count);
        }

        [Fact]
        public async Task Sequential_SameTimingEverywhere_WithMove()
        {
            WorkoutProgram p = await RunAsync(new GuidedProgramBuilder("help me build a new session"),
                "2", "Shuttle Runs, Squats, Skipping", "40/20 x 3", "1 min", "skip", "y");

            Assert.Equal(ExecutionMode.Sequential, p.Mode);
            Assert.Equal(new[] { 180, 180, 180 }, Enumerable.Range(0, 3).Select(i => p.TimelineForStation(i).TotalSeconds()));
            Assert.Equal((3 * 180) + (2 * 60), RunSeconds(p));
        }

        [Fact]
        public async Task Sequential_PerStationTimingAnswer()
        {
            WorkoutProgram p = await RunAsync(new GuidedProgramBuilder("create a new program"),
                "2", "Bike, Treadmill", "Bike: 40/20 x 8\nTreadmill: 1 min / 1 min x 4", "30", "Mixed", "y");

            Assert.Equal(480, p.TimelineForStation(0).TotalSeconds());
            Assert.Equal(480, p.TimelineForStation(1).TotalSeconds());
            Assert.Equal(60, p.TimelineForStation(1).Resolve()[0].Block.Seconds);
        }

        // ---------------------------------------------------------------- conversation behaviour

        [Fact]
        public async Task StationCount_SuggestsFromTheLibrary_MatchingTheFocus_WithTips()
        {
            bool aiAsked = false;
            Func<string, int, CancellationToken, Task<IReadOnlyList<string>>> ai =
                (req, n, ct) => { aiAsked = true; return Task.FromResult<IReadOnlyList<string>>(new[] { "Battle Ropes" }); };

            GuidedProgramBuilder b = new GuidedProgramBuilder("Help me create a new cardio focused program", ai);
            b.Start();
            await b.AnswerAsync("4");
            await b.AnswerAsync("5");
            WorkoutProgram p = b.BuildProgram();

            Assert.False(aiAsked); // the library had enough cardio exercises
            Assert.Equal(5, p.Stations.Count);
            Assert.Equal(5, p.Stations.Select(s => s.Name).Distinct().Count());
            foreach (StationDef s in p.Stations)
            {
                Exercise e = ExerciseLibrary.All.Single(x => x.Name == s.Name);
                Assert.True((e.Tags & ExerciseTag.Cardio) != 0, s.Name);
                StationTips.SplitCue(e.Cue, out string work, out string rest);
                Assert.Equal(work, s.WorkInstruction);
                Assert.False(string.IsNullOrWhiteSpace(s.RestInstruction), s.Name + " has no rest note");
            }
        }

        // ---------------------------------------------------------------- the "Buckers" chat (13 Nov 2026)

        private static async Task<GuidedProgramBuilder> BuckersSummaryAsync()
        {
            GuidedProgramBuilder b = new GuidedProgramBuilder("Help me create a new cardio focused program");
            b.Start();
            await b.AnswerAsync("4");
            await b.AnswerAsync("5");
            await b.AnswerAsync("45sec / 15sec x 4 reps");
            await b.AnswerAsync("1 min");
            await b.AnswerAsync("Buckers the ulitmate althlete");
            Assert.Equal(GuidedProgramBuilder.Step.Confirm, b.CurrentStep);
            return b;
        }

        [Fact]
        public async Task Confirm_ChangeStationsWithAList_KeepsTimingMoveAndName()
        {
            GuidedProgramBuilder b = await BuckersSummaryAsync();

            string reply = await b.AnswerAsync("change stations to be:\n1. Spin bike\n2. box step ups\n3. treadmill\n4. skipping");

            Assert.Equal(GuidedProgramBuilder.Step.Confirm, b.CurrentStep); // straight back to the summary
            Assert.Contains("Timing: 4x 45s/15s", reply);
            Assert.Contains("Move: 1:00", reply);
            Assert.Contains("Total: 19:00", reply); // 4 x 4:00 + 3 x 1:00 moves

            WorkoutProgram p = await RunAsync2(b, "y");
            Assert.Equal(new[] { "Spin bike", "Box Step Ups", "Treadmill", "Skipping" }, p.Stations.Select(s => s.Name).ToArray());
            Assert.Equal("Buckers the ulitmate althlete", p.Name);
            Assert.Equal(240, Flat(p.SharedTimeline).Sum(x => x.Seconds)); // 4 x (45 + 15) at each station
        }

        [Fact]
        public async Task Tips_ComeFromTheLibrary_ForTheTeachersOwnNames()
        {
            GuidedProgramBuilder b = await BuckersSummaryAsync();
            await b.AnswerAsync("change stations to be:\n1. Spin bike\n2. box step ups\n3. treadmill\n4. skipping");
            WorkoutProgram p = await RunAsync2(b, "y");

            // Work and rest notes kept apart (the 13 Nov chat had "easy spin on rest" in the work note).
            Assert.Equal("High cadence", p.Stations[0].WorkInstruction);
            Assert.Equal("Easy spin", p.Stations[0].RestInstruction);
            Assert.Equal("Whole foot on the step, swap lead legs", p.Stations[1].WorkInstruction);
            Assert.False(string.IsNullOrWhiteSpace(p.Stations[1].RestInstruction));
            Assert.Equal("Run at about 80% effort", p.Stations[2].WorkInstruction);
            Assert.Equal("Walk to recover", p.Stations[2].RestInstruction);
            Assert.Equal("Small jumps, turn the rope from the wrists", p.Stations[3].WorkInstruction);
            Assert.Contains("Rope down", p.Stations[3].RestInstruction);
            Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(b.Script, "RESTNOTE").Count);
            Assert.DoesNotContain("Timing:", b.Script);
            Assert.DoesNotContain("RESTNOTE \"Reps\"", b.Script);
        }

        [Fact]
        public async Task Tips_UnknownStation_AsksTheAi_AndTeacherCanOverride()
        {
            List<string> asked = new List<string>();
            Func<string, CancellationToken, Task<string>> cue = (name, ct) => { asked.Add(name); return Task.FromResult("Push the sled with straight arms"); };
            GuidedProgramBuilder b = new GuidedProgramBuilder("new circuit", null, null, cue);
            b.Start();
            await b.AnswerAsync("4");
            await b.AnswerAsync("Sled push, Skipping");
            await b.AnswerAsync("40/20 x 3");
            await b.AnswerAsync("30");
            string summary = await b.AnswerAsync("Test");

            Assert.Equal(new[] { "Sled push" }, asked.ToArray()); // Skipping came from the library
            Assert.Contains("Work: Push the sled with straight arms", summary);
            Assert.Contains("Rest: ", summary);

            string updated = await b.AnswerAsync("tip for 2: Keep the elbows tucked in");
            Assert.Contains("Work: Keep the elbows tucked in", updated);
            Assert.Contains("Rest: Walk it off", await b.AnswerAsync("rest tip for 2: Walk it off"));
            await b.AnswerAsync("tip for sled push: Low hips, drive the legs");
            WorkoutProgram p = await RunAsync2(b, "y");
            Assert.Equal("Low hips, drive the legs", p.Stations[0].WorkInstruction);
            Assert.Equal("Keep the elbows tucked in", p.Stations[1].WorkInstruction);
        }

        [Theory]
        [InlineData("Timing: 4x 45s/15s = 4:00", 4, 45, 15)]
        [InlineData("4x 45s/15s", 4, 45, 15)]
        [InlineData("4 x 45/15", 4, 45, 15)]
        [InlineData("4 rounds of 45/15", 4, 45, 15)]
        [InlineData("45sec / 15sec x 4 reps", 4, 45, 15)]
        public void Pattern_EchoedSummaryAndRoundsFirst(string text, int rounds, int work, int rest)
        {
            Assert.True(GuidedProgramBuilder.TryParsePattern(text, out Timeline t, out string workNote, out string restNote));
            var blocks = Flat(t);
            Assert.Equal(rounds * 2, blocks.Count);
            Assert.Equal((BlockType.Work, work), blocks[0]);
            Assert.Equal((BlockType.Rest, rest), blocks[1]);
            Assert.Null(workNote);
            Assert.Null(restNote);
        }

        [Fact]
        public void Pattern_BarePyramid_UsesTheUsualSteps()
        {
            Assert.True(GuidedProgramBuilder.TryParsePattern("pyramid", out Timeline t, out _, out _));
            Assert.Equal(new[] { 20, 20, 30, 20, 40, 20, 30, 20, 20, 20 }, Flat(t).Select(x => x.Seconds).ToArray());
        }

        [Fact]
        public async Task Confirm_InlineTimingMoveAndName()
        {
            GuidedProgramBuilder b = await BuckersSummaryAsync();
            Assert.Contains("Timing: 6x 40s/20s", await b.AnswerAsync("change the timing to 40/20 x 6"));
            Assert.Equal(GuidedProgramBuilder.Step.Confirm, b.CurrentStep);
            Assert.Contains("Move: 0:30", await b.AnswerAsync("move 30"));
            Assert.Contains("\"Cardio Blast\"", await b.AnswerAsync("change the name to Cardio Blast"));
            Assert.Equal(GuidedProgramBuilder.Step.Confirm, b.CurrentStep);
        }

        [Fact]
        public async Task Confirm_ChangeStationsWithoutAList_AsksThenReturnsToSummary()
        {
            GuidedProgramBuilder b = await BuckersSummaryAsync();
            await b.AnswerAsync("change stations");
            Assert.Equal(GuidedProgramBuilder.Step.Stations, b.CurrentStep);
            string reply = await b.AnswerAsync("Rower, Bike");
            Assert.Equal(GuidedProgramBuilder.Step.Confirm, b.CurrentStep);
            Assert.Contains("Timing: 4x 45s/15s", reply);
        }

        private static async Task<WorkoutProgram> RunAsync2(GuidedProgramBuilder builder, params string[] answers)
        {
            foreach (string answer in answers) await builder.AnswerAsync(answer);
            Assert.Equal(GuidedProgramBuilder.Step.Done, builder.CurrentStep);
            ProgramValidationResult result = new ProgramProposalValidator().Validate(builder.Script);
            Assert.True(result.IsValid, string.Join(" | ", result.Issues.Select(i => i.Message)) + "\n" + builder.Script);
            return result.ParsedProgram;
        }

        [Fact]
        public async Task BadAnswers_AreAskedAgain_AndConfirmCanGoBack()
        {
            GuidedProgramBuilder b = new GuidedProgramBuilder("new program");
            b.Start();
            Assert.Contains("1, 2, 3 or 4", await b.AnswerAsync("not sure"));
            await b.AnswerAsync("4");
            await b.AnswerAsync("A, B");
            Assert.Contains("couldn't read", await b.AnswerAsync("lots of fun"));
            await b.AnswerAsync("40/20 x 4");
            await b.AnswerAsync("30");
            string summary = await b.AnswerAsync("Test");
            Assert.Contains("Total: 8:30", summary); // 2 x 4:00 + 0:30 move

            await b.AnswerAsync("change the timing");
            Assert.Equal(GuidedProgramBuilder.Step.Timing, b.CurrentStep);
            string back = await b.AnswerAsync("tabata");
            Assert.Equal(GuidedProgramBuilder.Step.Confirm, b.CurrentStep); // move and name are kept
            Assert.Contains("Move: 0:30", back);
        }

        [Fact]
        public void ModeWordInOpeningRequest_SkipsModeQuestion()
        {
            GuidedProgramBuilder b = new GuidedProgramBuilder("Create a new parallel program");
            Assert.Contains("stations", b.Start());
            Assert.Equal(GuidedProgramBuilder.Step.Stations, b.CurrentStep);
        }

        [Theory]
        [InlineData("Help me create a new program", true, true)]
        [InlineData("create a 30 minute circuit", false, true)]
        [InlineData("create a 30 minute circuit", true, false)]
        [InlineData("Replace Burpees with Tuck Jumps", true, false)]
        [InlineData("How long is this workout?", true, false)]
        public void CreationIntent(string request, bool hasLoadedProgram, bool expected)
        {
            Assert.Equal(expected, GuidedProgramBuilder.IsCreationRequest(request, hasLoadedProgram));
        }
    }
}
