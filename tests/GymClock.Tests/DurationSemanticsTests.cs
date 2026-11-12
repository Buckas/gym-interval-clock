using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GymClock.Tests
{
    public class DurationSemanticsTests
    {
        [Fact]
        public void SharedDuration_UsesSharedTimelineRuntimeSequence()
        {
            WorkoutProgram program = new WorkoutProgram
            {
                Mode = ExecutionMode.Shared,
                SharedTimeline = BuildRepeatTimeline(2, 30, 15)
            };

            int total = program.BuildRuntimeSequence().Sum(x => Math.Max(0, x.Block.Seconds));

            // 2x (WORK 30 + REST 15) = 90s
            Assert.Equal(90, total);
        }

        [Fact]
        public void SequentialDuration_IncludesBetweenStationTimeline()
        {
            WorkoutProgram program = new WorkoutProgram
            {
                Mode = ExecutionMode.Sequential,
                BetweenStationsTimeline = new Timeline()
            };
            program.BetweenStationsTimeline.Add(new Block(BlockType.Move, 5));

            program.Stations.Add(new StationDef
            {
                Name = "S1",
                Source = TimingSource.Custom,
                CustomTimeline = BuildRepeatTimeline(1, 20, 10)
            });

            program.Stations.Add(new StationDef
            {
                Name = "S2",
                Source = TimingSource.Custom,
                CustomTimeline = BuildRepeatTimeline(1, 20, 10)
            });

            int total = program.BuildRuntimeSequence().Sum(x => Math.Max(0, x.Block.Seconds));

            // S1 (30) + BETWEEN (5) + S2 (30) = 65s
            Assert.Equal(65, total);
        }

        [Fact]
        public void ParallelDuration_UsesLongestStationLane()
        {
            WorkoutProgram program = new WorkoutProgram
            {
                Mode = ExecutionMode.Parallel,
                Rotations = 1 // a single pass: no rotation moves, just the stations side by side
            };

            Timeline station1 = new Timeline();
            station1.Add(new Block(BlockType.Work, 60));

            Timeline station2 = new Timeline();
            station2.Add(new Block(BlockType.Work, 30));
            station2.Add(new Block(BlockType.Rest, 15));

            program.Stations.Add(new StationDef
            {
                Name = "Fast",
                Source = TimingSource.Custom,
                CustomTimeline = station1
            });
            program.Stations.Add(new StationDef
            {
                Name = "Mixed",
                Source = TimingSource.Custom,
                CustomTimeline = station2
            });

            List<List<ResolvedBlock>> lanes = program.BuildParallelSequences();
            int longest = lanes.Select(l => l.Sum(b => Math.Max(0, b.Block.Seconds))).Max();

            // Longest lane is station1 = 60s
            Assert.Equal(60, longest);
        }

        [Fact]
        public void CircuitDuration_RepeatsSharedTimelinePerStation_WithBetweenMoves()
        {
            WorkoutProgram program = new WorkoutProgram
            {
                Mode = ExecutionMode.Circuit,
                SharedTimeline = BuildRepeatTimeline(2, 40, 20),
                BetweenStationsTimeline = new Timeline()
            };
            program.BetweenStationsTimeline.Add(new Block(BlockType.Move, 10));

            program.Stations.Add(new StationDef { Name = "A" });
            program.Stations.Add(new StationDef { Name = "B" });
            program.Stations.Add(new StationDef { Name = "C" });

            int total = program.BuildRuntimeSequence().Sum(x => Math.Max(0, x.Block.Seconds));

            // per station: 2x(40+20)=120; three stations => 360
            // between moves between station pairs: 2 x 10 = 20
            Assert.Equal(380, total);
        }

        private static Timeline BuildRepeatTimeline(int repeatCount, int workSeconds, int restSeconds)
        {
            Timeline timeline = new Timeline();
            RepeatGroup group = new RepeatGroup { Count = repeatCount };
            group.Blocks.Add(new Block(BlockType.Work, workSeconds));
            group.Blocks.Add(new Block(BlockType.Rest, restSeconds));
            timeline.Add(group);
            return timeline;
        }
    }
}
