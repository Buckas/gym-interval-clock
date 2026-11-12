using System;
using System.Linq;
using System.Reflection;
using GymClock.AI;
using Xunit;

namespace GymClock.Tests
{
    public class SampleProgramTests
    {
        private static string Sample(string constName)
        {
            FieldInfo field = typeof(ProgramLibrary).GetField(constName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);
            return (string)field.GetRawConstantValue();
        }

        [Theory]
        [InlineData("IntervalCardioCircuit")]
        [InlineData("PyramidCardioCircuit")]
        [InlineData("WholeGroupHiit")]
        [InlineData("RotatingGroupCircuit")]
        [InlineData("TeamTabataBlast")]
        [InlineData("MixedPaceStations")]
        [InlineData("EverestSession1")]
        [InlineData("EverestSession2")]
        [InlineData("EverestSession3")]
        public void EverySample_IsValid(string constName)
        {
            ProgramValidationResult result = new ProgramProposalValidator().Validate(Sample(constName));
            Assert.True(result.IsValid, string.Join(" | ", result.Issues.Select(i => i.Message)));
        }

        [Fact]
        public void EverestSession1_AllStationsRotateTogetherEvery8Minutes()
        {
            WorkoutProgram program = new ProgramProposalValidator().Validate(Sample("EverestSession1")).ParsedProgram;

            Assert.Equal(ExecutionMode.Parallel, program.Mode);
            Assert.Equal(new[] { "Bike", "Treadmill", "Skipping", "Floor" }, program.Stations.Select(s => s.Name));

            foreach (var lane in program.BuildParallelSequences())
            {
                Assert.Equal(35 * 60, lane.Sum(b => b.Block.Seconds));
                int[] moveStarts = lane
                    .Select((b, i) => new { b, start = lane.Take(i).Sum(x => x.Block.Seconds) })
                    .Where(x => x.b.Block.Type == BlockType.Move)
                    .Select(x => x.start)
                    .ToArray();
                Assert.Equal(new[] { 8 * 60, 17 * 60, 26 * 60 }, moveStarts);
            }
        }

        [Theory]
        [InlineData("EverestSession2", ExecutionMode.Circuit, 8, 39 * 60 + 30)]
        [InlineData("EverestSession3", ExecutionMode.Circuit, 4, 35 * 60)]
        public void EverestCircuits_HaveExpectedShape(string constName, ExecutionMode mode, int stations, int totalSeconds)
        {
            WorkoutProgram program = new ProgramProposalValidator().Validate(Sample(constName)).ParsedProgram;

            Assert.Equal(mode, program.Mode);
            Assert.Equal(stations, program.Stations.Count);
            Assert.Equal(totalSeconds, program.BuildRuntimeSequence().Sum(b => b.Block.Seconds));
        }
    }
}
