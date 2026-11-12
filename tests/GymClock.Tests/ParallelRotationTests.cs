using System;
using System.Collections.Generic;
using System.Linq;
using GymClock.AI;
using Xunit;

namespace GymClock.Tests
{
    public class ParallelRotationTests
    {
        private const string MixedLengths =
            "PROGRAM \"Mixed\"\nMODE PARALLEL\n\n"
            + "BETWEEN\n    MOVE 30 \"Rotate\"\nEND BETWEEN\n\n"
            + "STATION \"Tabata\"\n    REPEAT 8\n        WORK 20\n        REST 10\n    END\nEND STATION\n\n"   // 4:00
            + "STATION \"Row\"\n    WORK 600\nEND STATION\n";                                                   // 10:00

        private static int Length(List<ResolvedBlock> lane) => lane.Sum(b => Math.Max(0, b.Block.Seconds));

        [Fact]
        public void Rotations_DefaultToOnePerStation_AndAllStationsStayInSync()
        {
            WorkoutProgram program = ProgramScriptFormat.Parse(MixedLengths);
            List<List<ResolvedBlock>> lanes = program.BuildParallelSequences();

            // 2 stations -> 2 rotations of the longest station (10:00) + one 30s move.
            Assert.Equal(2, program.EffectiveRotations);
            Assert.All(lanes, lane => Assert.Equal(600 + 30 + 600, Length(lane)));

            // The short station recovers until the long one finishes, then everyone moves at 10:00.
            List<ResolvedBlock> tabata = lanes[0];
            ResolvedBlock padding = tabata[16];
            Assert.Equal(BlockType.Recovery, padding.Block.Type);
            Assert.Equal(360, padding.Block.Seconds);
            Assert.Equal(BlockType.Move, tabata[17].Block.Type);
            Assert.Equal(600, tabata.Take(17).Sum(b => b.Block.Seconds));
        }

        [Fact]
        public void RotationsLine_IsParsedValidatedAndWrittenBack()
        {
            string script = MixedLengths.Replace("MODE PARALLEL\n", "MODE PARALLEL\nROTATIONS 3\n");
            WorkoutProgram program = ProgramScriptFormat.Parse(script);

            Assert.Equal(3, program.Rotations);
            Assert.Equal((3 * 600) + (2 * 30), program.GetParallelRotationPlan().TotalSeconds);
            Assert.True(new ProgramProposalValidator().Validate(script).IsValid);

            string written = ProgramScriptFormat.ToScript(program);
            Assert.Contains("ROTATIONS 3", written);
            Assert.Contains("BETWEEN", written);
            Assert.Equal(3, ProgramScriptFormat.Parse(written).Rotations);
        }

        [Fact]
        public void RotationsLine_MustBePositive()
        {
            string script = MixedLengths.Replace("MODE PARALLEL\n", "MODE PARALLEL\nROTATIONS 0\n");
            Assert.False(new ProgramProposalValidator().Validate(script).IsValid);
        }

        [Fact]
        public void NoBetweenBlock_UsesDefaultMove()
        {
            WorkoutProgram program = ProgramScriptFormat.Parse(MixedLengths.Replace("BETWEEN\n    MOVE 30 \"Rotate\"\nEND BETWEEN\n\n", string.Empty));
            ParallelRotationPlan plan = program.GetParallelRotationPlan();
            Assert.Equal(WorkoutProgram.DefaultRotationMoveSeconds, plan.MoveSeconds);
        }

        [Theory]
        [InlineData(0, 1, false, 600)]
        [InlineData(599, 1, false, 1)]
        [InlineData(600, 1, true, 30)]
        [InlineData(615, 1, true, 15)]
        [InlineData(630, 2, false, 600)]
        [InlineData(1229, 2, false, 1)]
        public void Plan_Locate_ReportsRotationMoveAndCountdown(double elapsed, int rotation, bool moving, double toChange)
        {
            ParallelRotationPlan plan = ProgramScriptFormat.Parse(MixedLengths).GetParallelRotationPlan();
            plan.Locate(elapsed, out int r, out bool m, out double t);

            Assert.Equal(rotation, r);
            Assert.Equal(moving, m);
            Assert.Equal(toChange, t, 3);
        }
    }
}
