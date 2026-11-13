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
    public class StationTipsTests : IDisposable
    {
        private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gymclock-tips-" + Guid.NewGuid().ToString("N"));

        public StationTipsTests() { ExerciseCatalog.UseFolderForTesting(_folder); }

        public void Dispose()
        {
            ExerciseCatalog.UseFolderForTesting(null);
            try { System.IO.Directory.Delete(_folder, true); } catch { }
        }

        // The program from the "Buckers" chat (13 Nov 2026), with the bad "Timing: 4x" notes.
        private const string Buckers = @"PROGRAM ""Buckers the ulitmate althlete""
MODE CIRCUIT

REPEAT 4
    WORK 45s
    REST 15s
END

BETWEEN
    MOVE 1m ""Rotate to the next station""
END BETWEEN

STATION ""Spin bike""
    COLOUR #E53935
    WORKNOTE ""Timing: 4x""
END STATION

STATION ""box step ups""
    COLOUR #FB8C00
    WORKNOTE ""Timing: 4x""
END STATION

STATION ""treadmill""
    COLOUR #FDD835
    WORKNOTE ""Timing: 4x""
END STATION

STATION ""skipping""
    COLOUR #43A047
    WORKNOTE ""Timing: 4x""
END STATION
";

        private static WorkoutProgram Load()
        {
            ProgramValidationResult r = new ProgramProposalValidator().Validate(Buckers);
            Assert.True(r.IsValid, string.Join(" | ", r.Issues.Select(i => i.Message)));
            return r.ParsedProgram;
        }

        [Theory]
        [InlineData("Can you give me better coaching tips i.e. Text to show during work and during rest", true)]
        [InlineData("I need tips for each station can you give me examples", true)]
        [InlineData("I need the tips for the stations in this program. Give me examples for each of the current station", true)]
        [InlineData("Add coaching notes to every station", true)]
        [InlineData("I need 5 examples of resistance exercises to use with basic weights", false)]
        [InlineData("3\nMake sure you give work out notes", false)]
        [InlineData("Change the work note for station 2 to say \"Drive through the heel\"", false)]
        [InlineData("Replace the rest note for treadmill with walk slowly", false)]
        public void Intent(string request, bool expected)
        {
            Assert.Equal(expected, StationTips.IsTipsRequest(request));
        }

        [Fact]
        public void TipsRequest_IsNotTakenAsExerciseIdeas()
        {
            // The form checks tips first; this is the request that got 5 random exercises.
            Assert.True(StationTips.IsTipsRequest("I need tips for each station can you give me examples"));
        }

        [Fact]
        public async Task Suggest_GivesWorkAndRestForEveryStation_FromTheLibrary()
        {
            List<StationTip> tips = await StationTips.SuggestAsync(Load(), "tips for each station", null, CancellationToken.None);

            Assert.Equal(4, tips.Count);
            Assert.Equal("High cadence on work, easy spin on rest", tips[0].Work);
            Assert.Equal("Whole foot on the step, swap lead legs", tips[1].Work);
            Assert.Equal("Run at about 80% effort, walk to recover", tips[2].Work);
            Assert.Equal("Small jumps, turn the rope from the wrists", tips[3].Work);
            Assert.All(tips, t => Assert.False(string.IsNullOrWhiteSpace(t.Rest)));
            Assert.Equal("Easy pace - let your breathing settle", tips[0].Rest);
            Assert.Contains("Rope down", tips[3].Rest);
        }

        [Fact]
        public async Task Suggest_OnlyTheNamedStation_AndAiForUnknownNames()
        {
            WorkoutProgram p = Load();
            p.Stations[1].Name = "Sled push";
            List<string> asked = new List<string>();
            Func<string, CancellationToken, Task<string>> ai = (n, ct) => { asked.Add(n); return Task.FromResult("Low hips, drive with the legs."); };

            List<StationTip> tips = await StationTips.SuggestAsync(p, "give me tips for station 2", ai, CancellationToken.None);

            Assert.Single(tips);
            Assert.Equal("Low hips, drive with the legs", tips[0].Work);
            Assert.Equal(new[] { "Sled push" }, asked.ToArray());
        }

        [Fact]
        public async Task Apply_WritesNotes_AndScriptStaysValid()
        {
            WorkoutProgram p = Load();
            List<StationTip> tips = await StationTips.SuggestAsync(p, "tips", null, CancellationToken.None);

            IReadOnlyList<int> picks = StationTips.ParseApplyReply("apply 1, 3", p.Stations.Count);
            Assert.Equal(new[] { 0, 2 }, picks.ToArray());

            StationTips.Apply(p, tips, picks);
            string script = ProgramScriptFormat.ToScript(p);
            ProgramValidationResult r = new ProgramProposalValidator().Validate(script);
            Assert.True(r.IsValid);
            Assert.Equal("High cadence on work, easy spin on rest", r.ParsedProgram.Stations[0].WorkInstruction);
            Assert.Equal("Timing: 4x", r.ParsedProgram.Stations[1].WorkInstruction); // not picked
            Assert.Equal("Easy pace - let your breathing settle", r.ParsedProgram.Stations[2].RestInstruction);
            Assert.Equal(4 * 60, r.ParsedProgram.SharedTimeline.Resolve().Sum(b => b.Block.Seconds));
        }

        [Theory]
        [InlineData("APPLY", 4)]
        [InlineData("yes", 4)]
        [InlineData("add them", 4)]
        [InlineData("apply 2", 1)]
        public void ApplyReplies(string reply, int count)
        {
            Assert.Equal(count, StationTips.ParseApplyReply(reply, 4).Count);
        }

        [Fact]
        public void OtherReplies_AreNotApply()
        {
            Assert.Null(StationTips.ParseApplyReply("make the rest notes shorter", 4));
            Assert.True(StationTips.WantsThemApplied("Add coaching notes to every station"));
            Assert.False(StationTips.WantsThemApplied("Can you give me better coaching tips"));
        }
    }
}
