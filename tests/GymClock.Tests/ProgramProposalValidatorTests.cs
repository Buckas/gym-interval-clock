using System;
using GymClock.AI;
using Xunit;

namespace GymClock.Tests
{
    public class ProgramProposalValidatorTests
    {
        private readonly ProgramProposalValidator _validator = new ProgramProposalValidator();

        [Fact]
        public void ValidSequential_IntervalCardioCircuit_Passes()
        {
            string script = @"PROGRAM ""45/15 Interval Cardio Circuit""
MODE SEQUENTIAL

BETWEEN
    MOVE 60 ""Move to the next station""
END BETWEEN

STATION ""Treadmill""
REPEAT 8
    WORK 45
    REST 15
END
END STATION

STATION ""Exercise Cycle""
REPEAT 8
    WORK 45
    REST 15
END
END STATION
";

            ProgramValidationResult result = _validator.Validate(script);
            Assert.True(result.IsValid);
            Assert.NotNull(result.ParsedProgram);
        }

        [Fact]
        public void ValidSequential_Pyramid_Passes()
        {
            string script = @"PROGRAM ""Pyramid""
MODE SEQUENTIAL
STATION ""Burpees""
REPEAT 1
    WORK 20
    REST 10
    WORK 30
    REST 15
    WORK 40
    REST 20
END
END STATION
";
            ProgramValidationResult result = _validator.Validate(script);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ValidSharedTabata_Passes()
        {
            string script = @"PROGRAM ""Team Tabata Blast""
MODE SHARED
REPEAT 8
    WORK 20
    REST 10
END

STATION ""Squats""
COLOUR #E53935
END STATION
";
            ProgramValidationResult result = _validator.Validate(script);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ValidParallel_MixedDurations_Passes()
        {
            string script = @"PROGRAM ""Mixed Pace Stations""
MODE PARALLEL

STATION ""Tabata Burpees""
REPEAT 8
    WORK 20
    REST 10
END
END STATION

STATION ""Steady Row""
REPEAT 1
    WORK 600
END
END STATION
";
            ProgramValidationResult result = _validator.Validate(script);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ValidCircuit_SharedTiming_Passes()
        {
            string script = @"PROGRAM ""Rotating Group Circuit""
MODE CIRCUIT

REPEAT 4
    WORK 45
    REST 15
END

BETWEEN
    MOVE 20 ""Move clockwise to the next station""
END BETWEEN

STATION ""Station 1 - Kettlebell Swings""
COLOUR #E53935
END STATION

STATION ""Station 2 - Box Jumps""
COLOUR #FB8C00
END STATION
";
            ProgramValidationResult result = _validator.Validate(script);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ValidWholeGroupHiit_Passes()
        {
            string script = @"PROGRAM ""Whole Group HIIT""
MODE SEQUENTIAL
STATION ""Shuttle Runs""
REPEAT 3
    WORK 40
    RECOVERY 20
END
END STATION
";
            ProgramValidationResult result = _validator.Validate(script);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void ValidDurations_WithMinuteAndSecondWords_Passes()
        {
            string script = @"PROGRAM ""Word Units""
MODE SHARED
REPEAT 1
    WORK 10min
    REST 30sec
    RECOVERY 15seconds
END
STATION ""A""
END STATION";

            ProgramValidationResult result = _validator.Validate(script);
            Assert.True(result.IsValid);
        }

        [Fact]
        public void MissingProgram_Fails()
        {
            string script = @"MODE SHARED
WORK 30
REST 15";

            ProgramValidationResult result = _validator.Validate(script);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("Missing PROGRAM"));
        }

        [Fact]
        public void MissingMode_Fails()
        {
            string script = @"PROGRAM ""No Mode""
WORK 30
REST 15";
            ProgramValidationResult result = _validator.Validate(script);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("Missing MODE"));
        }

        [Fact]
        public void MissingEndStation_FailsWithLineInfo()
        {
            string script = @"PROGRAM ""Broken""
MODE SEQUENTIAL
STATION ""Rowing""
WORK 30";

            ProgramValidationResult result = _validator.Validate(script);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("Unclosed STATION"));
        }

        [Fact]
        public void UnknownCommand_FailsWithLineNumber()
        {
            string script = @"PROGRAM ""Broken""
MODE SHARED
PAUSE 10";

            ProgramValidationResult result = _validator.Validate(script);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.LineNumber == 3 && i.Message.Contains("Unknown command"));
        }

        [Fact]
        public void NegativeDuration_FailsWithLineNumber()
        {
            string script = @"PROGRAM ""Broken""
MODE SHARED
WORK -20";

            ProgramValidationResult result = _validator.Validate(script);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.LineNumber == 3 && i.Message.Contains("duration must be greater than zero"));
        }

        [Fact]
        public void ZeroRepeat_FailsWithLineNumber()
        {
            string script = @"PROGRAM ""Broken""
MODE SHARED
REPEAT 0
    WORK 20
END";

            ProgramValidationResult result = _validator.Validate(script);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.LineNumber == 3 && i.Message.Contains("REPEAT count"));
        }

        [Fact]
        public void InvalidColour_FailsWithLineNumber()
        {
            string script = @"PROGRAM ""Broken""
MODE SHARED
STATION ""A""
COLOUR #GGGGGG
END STATION";

            ProgramValidationResult result = _validator.Validate(script);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.LineNumber == 4 && i.Message.Contains("Invalid colour"));
        }

        [Fact]
        public void EmptyResponse_Fails()
        {
            ProgramValidationResult result = _validator.Validate(string.Empty);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("Missing PROGRAM"));
        }
    }
}
