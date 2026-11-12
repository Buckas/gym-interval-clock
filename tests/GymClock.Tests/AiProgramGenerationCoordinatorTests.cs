using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GymClock.AI;
using Xunit;

namespace GymClock.Tests
{
    public class AiProgramGenerationCoordinatorTests
    {
        [Fact]
        public async Task ValidFirstGeneration_ReturnsValidProposal_WithoutRepair()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[] { ValidSharedScript() });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiAssistantOptions options = new AiAssistantOptions { MaximumAutomaticRepairAttempts = 1 };
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, options);

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Create a short shared class",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(
                proposal.IsValid,
                proposal.Script + " || " + string.Join(" | ", proposal.Issues.Select(i => i.Message)));
            Assert.Single(fake.Calls);
        }

        [Fact]
        public async Task InvalidThenValidRepair_AttemptsOneRepair_AndReturnsValid()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[]
            {
                "PROGRAM \"Broken\"\nMODE SHARED\nWORK -10",
                ValidSharedScript()
            });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiAssistantOptions options = new AiAssistantOptions { MaximumAutomaticRepairAttempts = 1 };
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, options);

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Fix this",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.IsValid, string.Join(" | ", proposal.Issues.Select(i => i.Message)));
            Assert.Equal(2, fake.Calls.Count);

            // Checkpoint 8: exactly one repair attempt means exactly two total calls.
            Assert.Equal(2, fake.CallCount);
        }

        [Fact]
        public async Task InvalidThenInvalidRepair_StopsAfterOneRepair_AndReturnsInvalid()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[]
            {
                "PROGRAM \"Broken\"\nMODE SHARED\nWORK -10",
                "PROGRAM \"Still Broken\"\nMODE SHARED\nPAUSE 10"
            });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiAssistantOptions options = new AiAssistantOptions { MaximumAutomaticRepairAttempts = 1 };
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, options);

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Try repair",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.False(proposal.IsValid);
            Assert.Equal(2, fake.Calls.Count);
            Assert.Equal(2, fake.CallCount);
            Assert.Contains(proposal.Issues, i => i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public async Task MaximumAutomaticRepairAttempts_Zero_DoesNotAttemptRepair()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[]
            {
                "PROGRAM \"Broken\"\nMODE SHARED\nWORK -10",
                ValidSharedScript() // would be valid if a repair call happened
            });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiAssistantOptions options = new AiAssistantOptions { MaximumAutomaticRepairAttempts = 0 };
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, options);

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Try repair",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.False(proposal.IsValid);
            Assert.Equal(1, fake.CallCount);
        }

        [Fact]
        public async Task MaximumAutomaticRepairAttempts_One_DoesNotCallThirdTime()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[]
            {
                "PROGRAM \"Broken\"\nMODE SHARED\nWORK -10",
                "PROGRAM \"Still Broken\"\nMODE SHARED\nPAUSE 10",
                ValidSharedScript() // should never be consumed in Checkpoint 8 flow
            });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiAssistantOptions options = new AiAssistantOptions { MaximumAutomaticRepairAttempts = 1 };
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, options);

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Try repair",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.False(proposal.IsValid);
            Assert.Equal(2, fake.CallCount);
            Assert.Equal(1, fake.RemainingResponses);
        }

        [Fact]
        public async Task MarkdownFenceCleanup_RemovesFencesBeforeValidation()
        {
            string fenced = "```\n" + ValidSharedScript() + "\n```";
            FakeAiProgramService fake = new FakeAiProgramService(new[] { fenced });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Generate",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.IsValid, string.Join(" | ", proposal.Issues.Select(i => i.Message)));
            Assert.DoesNotContain("```", proposal.Script);
        }

        [Fact]
        public async Task AmbiguousPyramidPrompt_ReturnsClarificationQuestion_WithoutCallingAi()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[] { ValidSharedScript() });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "I want a pyramid program with cardio and resistance for 30 minutes.",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.RequiresClarification);
            Assert.Contains("Reply with a number", proposal.AssistantMessage);
            Assert.Equal(0, fake.CallCount);
        }

        [Fact]
        public async Task AmbiguousPyramidPrompt_WithBaselineScript_StillRequestsClarification()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[] { ValidSharedScript() });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "I want a pyramid program with cardio and resistance for 30 minutes.",
                ValidSharedScript(),
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.RequiresClarification);
            Assert.Equal(0, fake.CallCount);
        }

        [Fact]
        public async Task RevisionDurationPrompt_WithoutStationPolicy_RequestsStationPolicyClarification()
        {
            string baselineScript =
                "PROGRAM \"Baseline\"\n"
                + "MODE CIRCUIT\n"
                + "REPEAT 3\n"
                + "    WORK 30\n"
                + "    REST 15\n"
                + "END\n\n"
                + "STATION \"A\"\nEND STATION\n\n"
                + "STATION \"B\"\nEND STATION\n\n"
                + "STATION \"C\"\nEND STATION\n\n"
                + "STATION \"D\"\nEND STATION";

            FakeAiProgramService fake = new FakeAiProgramService(Array.Empty<string>());
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Revise the current program to increase total duration to about 60 minutes without changing mode.",
                baselineScript,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.RequiresClarification);
            Assert.Contains("station-change policy", proposal.AssistantMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, fake.CallCount);
        }

        [Fact]
        public async Task AmbiguousIntervalPrompt_ReturnsClarificationQuestion_WithoutCallingAi()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[] { ValidSharedScript() });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Create a 30-minute interval workout with cardio and strength blocks.",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.RequiresClarification);
            Assert.Equal(0, fake.CallCount);
        }

        [Fact]
        public async Task GenericProgramPrompt_WithoutStructure_ReturnsClarificationQuestion()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[] { ValidSharedScript() });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Build a 30-minute cardio and resistance program for my class.",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.RequiresClarification);
            Assert.Equal(0, fake.CallCount);
        }

        [Fact]
        public async Task CircuitPrompt_WithStationHint_DoesNotClarify()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[] { ValidEightStationCircuitScript() });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Create a circuit with 8 stations and 4 rounds.",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.False(proposal.RequiresClarification);
            Assert.True(proposal.IsValid);
            Assert.Equal(1, fake.CallCount);
        }

        [Fact]
        public async Task RequestedStationCountMismatch_TriggersRepairAttempt()
        {
            string wrongStationCount =
                "PROGRAM \"Wrong Count\"\n"
                + "MODE SHARED\n"
                + "REPEAT 10\n"
                + "    WORK 45\n"
                + "    REST 15\n"
                + "END\n"
                + "STATION \"Cardio - Jumping Jacks\"\nEND STATION\n"
                + "STATION \"Resistance - Squats\"\nEND STATION\n"
                + "STATION \"Cardio - High Knees\"\nEND STATION\n"
                + "STATION \"Resistance - Push Ups\"\nEND STATION";

            string correctedStationCount =
                wrongStationCount + "\n\nSTATION \"Cardio - Mountain Climbers\"\nEND STATION\n"
                + "STATION \"Resistance - Lunges\"\nEND STATION";

            FakeAiProgramService fake = new FakeAiProgramService(new[] { wrongStationCount, correctedStationCount });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiAssistantOptions options = new AiAssistantOptions { MaximumAutomaticRepairAttempts = 1 };
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, options);

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Create a MODE SHARED class with 6 stations and cardio plus resistance exercises.",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.IsValid);
            Assert.Equal(2, fake.CallCount);
            Assert.Equal(6, proposal.ParsedProgram.Stations.Count);
        }

        [Fact]
        public async Task PyramidRequirement_MissingDistinctWorkDurations_TriggersRepairAttempt()
        {
            string notPyramid =
                "PROGRAM \"Flat\"\n"
                + "MODE SHARED\n"
                + "REPEAT 12\n"
                + "    WORK 45\n"
                + "    REST 15\n"
                + "END\n"
                + "STATION \"Cardio - Jumping Jacks\"\nEND STATION\n"
                + "STATION \"Resistance - Push Ups\"\nEND STATION";

            string pyramid =
                "PROGRAM \"Pyramid\"\n"
                + "MODE SHARED\n"
                + "REPEAT 1\n"
                + "    WORK 30\n"
                + "    REST 15\n"
                + "    WORK 45\n"
                + "    REST 15\n"
                + "    WORK 60\n"
                + "    REST 15\n"
                + "    WORK 45\n"
                + "    REST 15\n"
                + "    WORK 30\n"
                + "    REST 15\n"
                + "END\n"
                + "STATION \"Cardio - High Knees\"\nEND STATION\n"
                + "STATION \"Resistance - Squats\"\nEND STATION\n"
                + "STATION \"Cardio - Jump Rope\"\nEND STATION\n"
                + "STATION \"Resistance - Lunges\"\nEND STATION\n"
                + "STATION \"Cardio - Mountain Climbers\"\nEND STATION\n"
                + "STATION \"Resistance - Push Ups\"\nEND STATION\n"
                + "STATION \"Cardio - Burpees\"\nEND STATION\n"
                + "STATION \"Resistance - Plank Holds\"\nEND STATION";

            FakeAiProgramService fake = new FakeAiProgramService(new[] { notPyramid, pyramid });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiAssistantOptions options = new AiAssistantOptions { MaximumAutomaticRepairAttempts = 1 };
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, options);

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Create a MODE SHARED cardio and resistance pyramid program.",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.IsValid);
            Assert.Equal(2, fake.CallCount);
        }

        [Fact]
        public async Task CircuitStrictPyramid_MalformedAiOutput_FallsBackToValidTemplate()
        {
            string malformed =
                "PROGRAM \"30 Minute Pyramid Workout\"\n"
                + "MODE CIRCUIT\n"
                + "REPEAT 5\n"
                + "STATION\n"
                + "STATION \"Cardio: Jumping Jacks\"\n"
                + "WORK 60\n"
                + "END STATION\n"
                + "STATION\n"
                + "STATION \"Resistance: Pushups\"\n"
                + "END STATION\n"
                + "STATION\n"
                + "STATION \"Cardio: High Knees\"\n"
                + "END STATION\n"
                + "END";

            FakeAiProgramService fake = new FakeAiProgramService(new[] { malformed });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiAssistantOptions options = new AiAssistantOptions
            {
                MaximumAutomaticRepairAttempts = 0,
                MaximumGenerationAttempts = 1
            };
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, options);

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "i want a pyramid program with a combination of cardio and resistance training. we have 30 minutes to complete this.\n"
                + "Use MODE CIRCUIT.\n"
                + "Use 6 stations and 5 rounds.\n"
                + "Keep total duration very close to the requested time.\n"
                + "Include explicit cardio and resistance exercises in station names and/or notes.",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.IsValid, string.Join(" | ", proposal.Issues.Select(i => i.Message)));
            Assert.Contains("MODE CIRCUIT", proposal.Script, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(6, proposal.ParsedProgram.Stations.Count);

            int durationSeconds = proposal.ParsedProgram.BuildRuntimeSequence().Sum(x => Math.Max(0, x.Block.Seconds));
            Assert.InRange(durationSeconds, 25 * 60, 35 * 60);

            string[] stationNames = proposal.ParsedProgram.Stations.Select(s => s.Name ?? string.Empty).ToArray();
            Assert.Contains(stationNames, n => n.IndexOf("Cardio", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.Contains(stationNames, n => n.IndexOf("Resistance", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public async Task CommonMalformedScriptLines_AreNormalized_BeforeValidation()
        {
            string malformed =
                "PROGRAM 30 Minute Cardio & Resistance Pyramid\n"
                + "MODE SHARED\n\n"
                + "WORK 60\n"
                + "REST 30\n\n"
                + "STATION Jumping Jacks\n"
                + "COLOUR\n"
                + "END STATION\n\n"
                + "STATION Push Ups\n"
                + "END STATION\n\n"
                + "STATION \"Mountain Climbers\n"
                + "END STATION";

            FakeAiProgramService fake = new FakeAiProgramService(new[] { malformed });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Create a shared cardio and resistance class.",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.IsValid);
            Assert.DoesNotContain("\nCOLOUR\n", proposal.Script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("STATION \"Jumping Jacks\"", proposal.Script);
            Assert.Contains("STATION \"Mountain Climbers\"", proposal.Script);
        }

        [Fact]
        public async Task CircuitRevision_MissingRepeatEnd_AndStrayEndBeforeStationEnd_AreNormalized()
        {
            string malformedRevision =
                "PROGRAM \"30 Minute Cardio & Resistance Pyramid\"\n"
                + "MODE CIRCUIT\n\n"
                + "REPEAT 5\n"
                + "    WORK 60\n"
                + "    REST 30\n"
                + "    WORK 90\n"
                + "    REST 30\n"
                + "    WORK 120\n"
                + "    REST 30\n"
                + "    WORK 90\n"
                + "    REST 30\n"
                + "    WORK 60\n"
                + "    REST 30\n\n"
                + "STATION \"Cardio: Jumping Jacks\"\n"
                + "    COLOUR #FF80FF\n"
                + "END STATION\n\n"
                + "STATION \"Resistance: Push Ups\"\n"
                + "END STATION\n\n"
                + "STATION \"Cardio: High Knees\"\n"
                + "END\n"
                + "END STATION";

            FakeAiProgramService fake = new FakeAiProgramService(new[] { malformedRevision });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(
                fake,
                validator,
                new AiAssistantOptions { MaximumAutomaticRepairAttempts = 0, MaximumGenerationAttempts = 1 });

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Revise the current program to increase total duration to about 60 minutes without changing mode.",
                "PROGRAM \"Baseline\"\nMODE CIRCUIT\nREPEAT 1\n    WORK 30\n    REST 15\nEND\n\nSTATION \"A\"\nEND STATION",
                new ConversationState(),
                CancellationToken.None);

            Assert.DoesNotContain(
                proposal.Issues,
                i => i.Message.IndexOf("Program has no executable timed content", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain(
                proposal.Issues,
                i => i.Message.IndexOf("Unclosed REPEAT block", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.Contains("REPEAT", proposal.Script, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("END", proposal.Script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("END\nEND STATION", proposal.Script, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task RequestedDurationMismatch_TriggersRepairAttempt()
        {
            string tooShort =
                "PROGRAM \"Short\"\n"
                + "MODE SHARED\n"
                + "REPEAT 1\n"
                + "    WORK 30\n"
                + "    REST 15\n"
                + "END\n"
                + "STATION \"Cardio - Jumping Jacks\"\nEND STATION\n"
                + "STATION \"Resistance - Push Ups\"\nEND STATION";

            string thirtyMinutes =
                "PROGRAM \"30 Minute Mix\"\n"
                + "MODE SHARED\n"
                + "REPEAT 24\n"
                + "    WORK 45\n"
                + "    REST 30\n"
                + "END\n"
                + "STATION \"Cardio - High Knees\"\nEND STATION\n"
                + "STATION \"Resistance - Squats\"\nEND STATION";

            FakeAiProgramService fake = new FakeAiProgramService(new[] { tooShort, thirtyMinutes });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiAssistantOptions options = new AiAssistantOptions { MaximumAutomaticRepairAttempts = 1 };
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, options);

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Create a MODE SHARED 30 minute cardio and resistance program.",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.IsValid);
            Assert.Equal(2, fake.CallCount);
            Assert.Contains("REPEAT 24", proposal.Script);
        }

        [Fact]
        public async Task DurationWithinFiveMinutes_DoesNotForceRepair()
        {
            string twentySixMinutes =
                "PROGRAM \"26 Minute Mix\"\n"
                + "MODE SHARED\n"
                + "REPEAT 13\n"
                + "    WORK 60\n"
                + "    REST 60\n"
                + "END\n"
                + "STATION \"Cardio - High Knees\"\nEND STATION\n"
                + "STATION \"Resistance - Push Ups\"\nEND STATION";

            FakeAiProgramService fake = new FakeAiProgramService(new[] { twentySixMinutes, ValidSharedScript() });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiAssistantOptions options = new AiAssistantOptions { MaximumAutomaticRepairAttempts = 1 };
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, options);

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Create a MODE SHARED 30 minute cardio and resistance program.",
                string.Empty,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.IsValid);
            Assert.Equal(1, fake.CallCount);
        }

        [Fact]
        public async Task Cancellation_ThrowsCancellationException()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[] { ValidSharedScript() }, delayMs: 300);
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, new AiAssistantOptions());

            using CancellationTokenSource cts = new CancellationTokenSource(50);

            Exception ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await coordinator.GenerateAsync("Generate", string.Empty, new ConversationState(), cts.Token);
            });

            Assert.True(ex is TaskCanceledException || ex is OperationCanceledException);
        }

        [Fact]
        public async Task Checkpoint7_RotatingCircuitPrompt_ReturnsValidProposal_WithoutApplyingToEditor()
        {
            string baselineScript = "PROGRAM \"Baseline\"\nMODE SHARED\nWORK 30\nREST 15";
            string prompt =
                "Create an eight-station rotating circuit for 24 students in groups of three. "
                + "Use four rounds, 45 seconds work, 15 seconds rest and 20 seconds to move clockwise. "
                + "Give each station a different colour.";

            FakeAiProgramService fake = new FakeAiProgramService(new[] { ValidEightStationCircuitScript() });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, new AiAssistantOptions());
            ConversationState conversation = new ConversationState();

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                prompt,
                baselineScript,
                conversation,
                CancellationToken.None);

            Assert.True(proposal.IsValid);
            Assert.NotNull(proposal.ParsedProgram);
            Assert.Equal(ExecutionMode.Circuit, proposal.ParsedProgram.Mode);
            Assert.Equal(8, proposal.ParsedProgram.Stations.Count);

            int distinctColours = proposal.ParsedProgram.Stations
                .Select(s => s.Colour)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .Count();
            Assert.Equal(8, distinctColours);

            // Generation must not mutate any editor state; explicit Apply is separate.
            Assert.Equal("PROGRAM \"Baseline\"\nMODE SHARED\nWORK 30\nREST 15", baselineScript);
        }

        [Fact]
        public async Task Checkpoint9_ConversationalRevision_AppliesRequestedChanges_AndPreservesOthers()
        {
            string baselineScript =
                "PROGRAM \"Rotating Group Circuit\"\n"
                + "MODE CIRCUIT\n\n"
                + "REPEAT 4\n"
                + "    WORK 45\n"
                + "    REST 15\n"
                + "END\n\n"
                + "BETWEEN\n"
                + "    MOVE 20 \"Move clockwise to the next station\"\n"
                + "END BETWEEN\n\n"
                + "STATION \"Station 1\"\nCOLOUR #E53935\nEND STATION\n\n"
                + "STATION \"Station 2\"\nCOLOUR #FB8C00\nEND STATION\n\n"
                + "STATION \"Station 3\"\nCOLOUR #FDD835\nEND STATION\n\n"
                + "STATION \"Station 4\"\nCOLOUR #43A047\nEND STATION";

            string revisedScript =
                "PROGRAM \"Rotating Group Circuit\"\n"
                + "MODE CIRCUIT\n\n"
                + "REPEAT 4\n"
                + "    WORK 40\n"
                + "    REST 15\n"
                + "END\n\n"
                + "BETWEEN\n"
                + "    MOVE 20 \"Move clockwise to the next station\"\n"
                + "END BETWEEN\n\n"
                + "STATION \"Station 1\"\nCOLOUR #E53935\nEND STATION\n\n"
                + "STATION \"Station 2\"\nCOLOUR #FB8C00\nEND STATION\n\n"
                + "STATION \"Skipping\"\nCOLOUR #FDD835\nEND STATION\n\n"
                + "STATION \"Station 4\"\nCOLOUR #43A047\nEND STATION";

            FakeAiProgramService fake = new FakeAiProgramService(new[] { revisedScript });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Change the work periods to 40 seconds and replace station 3 with skipping. Keep everything else the same.",
                baselineScript,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.IsValid);
            Assert.NotNull(proposal.ParsedProgram);

            WorkoutProgram program = proposal.ParsedProgram;

            // Requested changes
            var sharedBlocks = program.SharedTimeline.Resolve();
            Assert.Contains(sharedBlocks, b => b.Block.Type == BlockType.Work && b.Block.Seconds == 40);
            Assert.Equal("Skipping", program.Stations[2].Name);

            // Unchanged properties
            Assert.Equal(ExecutionMode.Circuit, program.Mode);
            Assert.Equal(4, program.Stations.Count);
            Assert.Contains(sharedBlocks, b => b.Block.Type == BlockType.Rest && b.Block.Seconds == 15);
            Assert.Contains(program.BetweenStationsTimeline.Resolve(), b => b.Block.Type == BlockType.Move && b.Block.Seconds == 20);
            Assert.Equal("Station 1", program.Stations[0].Name);
            Assert.Equal("Station 2", program.Stations[1].Name);
            Assert.Equal("Station 4", program.Stations[3].Name);
        }

        [Fact]
        public async Task Revision_WithoutChangingMode_PreservesBaselineModeAndStationCount()
        {
            string baselineScript =
                "PROGRAM \"30 Minute Cardio & Resistance Pyramid\"\n"
                + "MODE CIRCUIT\n\n"
                + "REPEAT 5\n"
                + "    WORK 60\n"
                + "    REST 30\n"
                + "    WORK 90\n"
                + "    REST 30\n"
                + "    WORK 120\n"
                + "    REST 30\n"
                + "    WORK 90\n"
                + "    REST 30\n"
                + "    WORK 60\n"
                + "    REST 30\n"
                + "END\n\n"
                + "STATION \"Cardio: Jumping Jacks\"\nEND STATION\n\n"
                + "STATION \"Resistance: Push Ups\"\nEND STATION\n\n"
                + "STATION \"Cardio: High Knees\"\nEND STATION\n\n"
                + "STATION \"Resistance: Bodyweight Squats\"\nEND STATION\n\n"
                + "STATION \"Cardio: Mountain Climbers\"\nEND STATION\n\n"
                + "STATION \"Resistance: Lunges\"\nEND STATION";

            string aiRevisionThatDropsStations =
                "PROGRAM \"30 Minute Cardio & Resistance Pyramid\"\n"
                + "MODE CIRCUIT\n\n"
                + "REPEAT 5\n"
                + "    WORK 90\n"
                + "    REST 30\n"
                + "    WORK 120\n"
                + "    REST 30\n"
                + "    WORK 90\n"
                + "    REST 30\n"
                + "    WORK 180\n"
                + "    REST 30\n"
                + "    WORK 120\n"
                + "    REST 30\n"
                + "    WORK 90\n"
                + "    REST 30\n"
                + "    WORK 60\n"
                + "    REST 30\n"
                + "END\n\n"
                + "STATION \"Cardio: Jumping Jacks\"\nEND STATION\n\n"
                + "STATION \"Resistance: Push Ups\"\nEND STATION\n\n"
                + "STATION \"Cardio: High Knees\"\nEND STATION";

            FakeAiProgramService fake = new FakeAiProgramService(new[] { aiRevisionThatDropsStations });
            ProgramProposalValidator validator = new ProgramProposalValidator();
            AiAssistantOptions options = new AiAssistantOptions
            {
                MaximumAutomaticRepairAttempts = 0,
                MaximumGenerationAttempts = 1
            };
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(fake, validator, options);

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Revise the current program to increase total duration to about 60 minutes without changing mode.\n"
                + "Preserve station count and station details unchanged.",
                baselineScript,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.IsValid, string.Join(" | ", proposal.Issues.Select(i => i.Message)));
            Assert.NotNull(proposal.ParsedProgram);
            Assert.Equal(ExecutionMode.Circuit, proposal.ParsedProgram.Mode);
            Assert.Equal(6, proposal.ParsedProgram.Stations.Count);
            Assert.True(proposal.LeftUnchanged);
            Assert.Equal(baselineScript, proposal.Script);
        }

        // Regression: a station-swap request on a CIRCUIT program used to come back as the
        // canned SHARED template (different timings, colours stripped).
        private const string ColouredPyramidCircuit =
            "PROGRAM \"30 Minute Cardio & Resistance Pyramid\"\n"
            + "MODE CIRCUIT\n\n"
            + "REPEAT 5\n"
            + "    WORK 6s\n    REST 5s\n    WORK 9s\n    REST 5s\n    WORK 12s\n    REST 5s\n"
            + "    WORK 9s\n    REST 5s\n    WORK 6s\n    REST 5s\n"
            + "END\n\n"
            + "STATION \"Cardio: Jumping Jacks\"\n    COLOUR #FF80FF\nEND STATION\n\n"
            + "STATION \"Resistance: Push Ups\"\n    COLOUR #FF00FF\nEND STATION\n\n"
            + "STATION \"Cardio: High Knees\"\n    COLOUR #FF0080\nEND STATION\n\n"
            + "STATION \"Resistance: Bodyweight Squats\"\n    COLOUR #8000FF\nEND STATION\n\n"
            + "STATION \"Cardio: Mountain Climbers\"\n    COLOUR #400080\nEND STATION\n\n"
            + "STATION \"Resistance: Lunges\"\n    COLOUR #FFFFFF\nEND STATION";

        [Fact]
        public async Task Revision_UnknownQuotedStation_RepliesWithStationList_WithoutCallingAi()
        {
            FakeAiProgramService fake = new FakeAiProgramService(Array.Empty<string>());
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(
                fake, new ProgramProposalValidator(), new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "I want a different station to replace \"Skater Bounds\", what do you suggest?",
                ColouredPyramidCircuit,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.LeftUnchanged);
            Assert.Equal(ColouredPyramidCircuit, proposal.Script);
            Assert.Equal(0, fake.CallCount);
            Assert.Contains("Skater Bounds", proposal.AssistantMessage);
            Assert.Contains("Cardio: Mountain Climbers", proposal.AssistantMessage);
        }

        [Fact]
        public async Task Revision_AllAttemptsInvalid_LeavesProgramUnchanged_InsteadOfTemplateFallback()
        {
            string prose = "I suggest replacing it with Burpees, which are a great cardio exercise.";
            string templateLike =
                "PROGRAM \"30 Minute Cardio & Resistance Pyramid\"\nMODE SHARED\n\n"
                + "REPEAT 6\n    WORK 30\n    REST 15\n    WORK 45\n    REST 15\n    WORK 60\n    REST 15\nEND\n\n"
                + "STATION \"Cardio: Jumping Jacks\"\nEND STATION\n\nSTATION \"Resistance: Push Ups\"\nEND STATION\n\n"
                + "STATION \"Cardio: Burpees\"\nEND STATION\n\nSTATION \"Resistance: Bodyweight Squats\"\nEND STATION\n\n"
                + "STATION \"Cardio: Mountain Climbers\"\nEND STATION\n\nSTATION \"Resistance: Lunges\"\nEND STATION";

            FakeAiProgramService fake = new FakeAiProgramService(new[] { prose, templateLike });
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(
                fake, new ProgramProposalValidator(), new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Make the station names more fun for the kids",
                ColouredPyramidCircuit,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.LeftUnchanged);
            Assert.Equal(ColouredPyramidCircuit, proposal.Script);
            Assert.Equal(2, fake.CallCount);
            Assert.Equal(AiChatRole.System, fake.Calls[0][0].Role);
            Assert.Contains("REVISION", fake.Calls[0][0].Content);
        }

        [Fact]
        public async Task Revision_MinimalStationSwap_IsAccepted_AndChangesAreSummarised()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[] { "Burpees | Chest to floor, jump at the top" });
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(
                fake, new ProgramProposalValidator(), new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Replace \"Cardio: High Knees\" with something else",
                ColouredPyramidCircuit,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.IsValid, string.Join(" | ", proposal.Issues.Select(i => i.Message)));
            Assert.False(proposal.LeftUnchanged);
            Assert.Equal(ExecutionMode.Circuit, proposal.ParsedProgram.Mode);
            Assert.Equal("#FF0080", proposal.ParsedProgram.Stations[2].Colour, ignoreCase: true);
            Assert.Contains("Station 3: \"Cardio: High Knees\" -> \"Cardio: Burpees\"", proposal.AssistantMessage);
            Assert.Equal(1, fake.CallCount);
            Assert.Equal("Chest to floor, jump at the top", proposal.ParsedProgram.Stations[2].WorkInstruction);
        }

        [Fact]
        public async Task Revision_ExplicitSwap_IsAppliedWithoutCallingAi()
        {
            FakeAiProgramService fake = new FakeAiProgramService(Array.Empty<string>());
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(
                fake, new ProgramProposalValidator(), new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Swap \"Resistance: Lunges\" for Glute Bridges",
                ColouredPyramidCircuit,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.IsValid, string.Join(" | ", proposal.Issues.Select(i => i.Message)));
            Assert.Equal(0, fake.CallCount);
            Assert.Equal(ColouredPyramidCircuit.Replace("Resistance: Lunges", "Resistance: Glute Bridges"), proposal.Script);
        }

        [Fact]
        public async Task Revision_DroppingColours_IsRejected()
        {
            string noColours = string.Join("\n", ColouredPyramidCircuit.Split('\n').Where(l => !l.Trim().StartsWith("COLOUR")));
            FakeAiProgramService fake = new FakeAiProgramService(new[] { noColours, noColours });
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(
                fake, new ProgramProposalValidator(), new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Add a short coaching note to every station",
                ColouredPyramidCircuit,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.LeftUnchanged);
            Assert.Equal(ColouredPyramidCircuit, proposal.Script);
        }

        private const string WholeGroupHiitSequential =
            "PROGRAM \"Whole Group HIIT\"\nMODE SEQUENTIAL\n\n"
            + "BETWEEN\n    MOVE 1m \"Move to the next station\"\nEND BETWEEN\n\n"
            + "STATION \"Shuttle Runs\"\n    WORKNOTE \"Go at 80% and keep the pace\"\n    RESTNOTE \"Walk to recover\"\n    REPEAT 3\n        WORK 40s\n        RECOVERY 20s\n    END\nEND STATION\n\n"
            + "STATION \"Bodyweight Squats\"\n    WORKNOTE \"Use what weight is right for you\"\n    RESTNOTE \"Walk to recover\"\n    REPEAT 3\n        WORK 40s\n        RECOVERY 20s\n    END\nEND STATION\n\n"
            + "STATION \"Skipping\"\n    WORKNOTE \"Go as hard as you can - keep skipping\"\n    RESTNOTE \"Rest and take in the deep breaths\"\n    REPEAT 3\n        WORK 40s\n        RECOVERY 20s\n    END\nEND STATION\n\n"
            + "STATION \"Step-Ups\"\n    WORKNOTE \"Swap alternative feet to step up on i.e Start with left and then do right\"\n    RESTNOTE \"Keep moving to recover\"\n    REPEAT 3\n        WORK 40s\n        RECOVERY 20s\n    END\nEND STATION\n\n"
            + "STATION \"Mountain Climbers\"\n    WORKNOTE \"This always hurts - keep it going the whole time\"\n    REPEAT 3\n        WORK 40s\n        RECOVERY 20s\n    END\nEND STATION\n\n"
            + "STATION \"Skater Bounds\"\n    REPEAT 3\n        WORK 40s\n        RECOVERY 20s\n    END\nEND STATION\n\n"
            + "STATION \"Burpees\"\n    REPEAT 3\n        WORK 40s\n        RECOVERY 20s\n    END\nEND STATION\n\n"
            + "STATION \"High Knees\"\n    REPEAT 3\n        WORK 40s\n        RECOVERY 20s\n    END\nEND STATION\n";

        [Fact]
        public void WholeGroupHiit_BaselineIsValid()
        {
            ProgramValidationResult r = new ProgramProposalValidator().Validate(WholeGroupHiitSequential);
            Assert.True(r.IsValid, string.Join(" | ", r.Issues.Select(i => i.Message)));
        }

        [Fact]
        public async Task WholeGroupHiit_SkaterBoundsSwap_NeverReturnsTemplate()
        {
            string prose = "I suggest Lateral Lunges as a replacement for Skater Bounds.";
            FakeAiProgramService fake = new FakeAiProgramService(new[] { prose, prose });
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(
                fake, new ProgramProposalValidator(), new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "I want a different station to replace \"Skater Bounds\", what do you suggest?",
                WholeGroupHiitSequential,
                new ConversationState(),
                CancellationToken.None);

            Assert.True(proposal.LeftUnchanged, proposal.Script);
            Assert.Equal(WholeGroupHiitSequential, proposal.Script);
            Assert.Equal(2, fake.CallCount);
        }

        [Fact]
        public async Task WholeGroupHiit_SkaterBoundsSwap_RenamesOnlyThatStation()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[] { "Lateral Lunges | Sink low and push off the outside foot" });
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(
                fake, new ProgramProposalValidator(), new AiAssistantOptions());

            AiProgramProposal proposal = await coordinator.GenerateAsync(
                "Swap \"Skater Bounds\" for something else",
                WholeGroupHiitSequential,
                new ConversationState(),
                CancellationToken.None);

            string expected = WholeGroupHiitSequential.Replace(
                "STATION \"Skater Bounds\"\n",
                "STATION \"Lateral Lunges\"\n    WORKNOTE \"Sink low and push off the outside foot\"\n");
            Assert.True(proposal.IsValid, string.Join(" | ", proposal.Issues.Select(i => i.Message)));
            Assert.Equal(expected, proposal.Script);
            Assert.Equal(1, fake.CallCount);
            Assert.Contains("Lateral Lunges", proposal.AssistantMessage);
        }

        [Theory]
        [InlineData("I want to change \"Skipping\" to something else. Do you have a suggestion?", null)]
        [InlineData("Can you give me a different exercise for Skipping?", null)]
        [InlineData("Change Skipping to Jump Squats", "Jump Squats")]
        [InlineData("replace skipping with Battle Ropes", "Battle Ropes")]
        public void StationSwap_IsDetected(string request, string expectedReplacement)
        {
            WorkoutProgram baseline = new ProgramProposalValidator().Validate(WholeGroupHiitSequential).ParsedProgram;

            Assert.True(AiProgramGenerationCoordinator.TryDetectStationSwap(request, baseline, out int index, out string replacement));
            Assert.Equal("Skipping", baseline.Stations[index].Name);
            Assert.Equal(expectedReplacement, replacement);
        }

        [Theory]
        [InlineData("Change the colour of Skipping to red")]
        [InlineData("Change the work time on Skipping to 30 seconds")]
        [InlineData("Change the note on Skipping")]
        public void StationSwap_NotDetected_ForOtherEdits(string request)
        {
            WorkoutProgram baseline = new ProgramProposalValidator().Validate(WholeGroupHiitSequential).ParsedProgram;
            Assert.False(AiProgramGenerationCoordinator.TryDetectStationSwap(request, baseline, out _, out _));
        }

        [Fact]
        public async Task SuggestionQuestion_OffersOptions_WithoutChanging_ThenNumberAppliesChoice()
        {
            FakeAiProgramService fake = new FakeAiProgramService(new[]
            {
                "1. Jump Squats | Explode up and land softly\n2. Jump Rope | Stay light on your feet\n3. Tuck Jumps | Knees to chest"
            });
            AiProgramGenerationCoordinator coordinator = new AiProgramGenerationCoordinator(
                fake, new ProgramProposalValidator(), new AiAssistantOptions());
            ConversationState conversation = new ConversationState();

            AiProgramProposal suggestion = await coordinator.GenerateAsync(
                "I want to change \"Skipping\" to something else. Do you have a suggestion?",
                WholeGroupHiitSequential, conversation, CancellationToken.None);

            Assert.True(suggestion.LeftUnchanged);
            Assert.Equal(WholeGroupHiitSequential, suggestion.Script);
            Assert.Contains("1) Jump Squats", suggestion.AssistantMessage);
            Assert.Contains("2) Jump Rope", suggestion.AssistantMessage);
            Assert.Contains("3) Tuck Jumps", suggestion.AssistantMessage);
            Assert.Contains("Nothing has been changed", suggestion.AssistantMessage);

            AiProgramProposal applied = await coordinator.GenerateAsync(
                "2", WholeGroupHiitSequential, conversation, CancellationToken.None);

            Assert.True(applied.IsValid, string.Join(" | ", applied.Issues.Select(i => i.Message)));
            Assert.False(applied.LeftUnchanged);
            Assert.Equal(1, fake.CallCount); // the pick itself needs no AI call
            Assert.Equal("Jump Rope", applied.ParsedProgram.Stations[2].Name);
            Assert.Equal("Stay light on your feet", applied.ParsedProgram.Stations[2].WorkInstruction);
            Assert.Equal("Rest and take in the deep breaths", applied.ParsedProgram.Stations[2].RestInstruction);
        }

        [Theory]
        [InlineData("2", "Jump Rope")]
        [InlineData("option 3", "Tuck Jumps")]
        [InlineData("the first one", "Jump Squats")]
        [InlineData("Let's go with jump rope", "Jump Rope")]
        public void PendingOption_IsPicked(string reply, string expected)
        {
            WorkoutProgram baseline = new ProgramProposalValidator().Validate(WholeGroupHiitSequential).ParsedProgram;
            PendingStationSuggestion pending = new PendingStationSuggestion
            {
                StationName = "Skipping",
                Options = new[] { new StationOption("Jump Squats", null), new StationOption("Jump Rope", null), new StationOption("Tuck Jumps", null) }
            };

            Assert.True(AiProgramGenerationCoordinator.TryPickPendingOption(reply, pending, baseline, out StationOption pick));
            Assert.Equal(expected, pick.Name);
        }

        [Fact]
        public void PendingOption_NotPicked_WhenReplyTargetsAnotherStation()
        {
            WorkoutProgram baseline = new ProgramProposalValidator().Validate(WholeGroupHiitSequential).ParsedProgram;
            PendingStationSuggestion pending = new PendingStationSuggestion
            {
                StationName = "Skipping",
                Options = new[] { new StationOption("Jump Rope", null) }
            };

            Assert.False(AiProgramGenerationCoordinator.TryPickPendingOption("Replace Burpees with Jump Rope", pending, baseline, out _));
        }

        [Theory]
        [InlineData("I want to change \"Skipping\" to something else. Do you have a suggestion?", true)]
        [InlineData("What could I replace Skipping with?", true)]
        [InlineData("Any ideas for a different exercise for Skipping", true)]
        [InlineData("Change Skipping to something else", false)]
        [InlineData("Replace Skipping with a different exercise", false)]
        public void SuggestionIntent_IsDistinguishedFromInstruction(string request, bool isSuggestion)
        {
            Assert.Equal(isSuggestion, AiProgramGenerationCoordinator.IsSuggestionRequest(request));
        }

        private static string ValidSharedScript()
        {
            return "PROGRAM \"Shared\"\nMODE SHARED\nREPEAT 4\n    WORK 30\n    REST 15\nEND\n\nSTATION \"A\"\nCOLOUR #E53935\nWORKNOTE \"Push pace\"\nEND STATION";
        }

        private static string ValidEightStationCircuitScript()
        {
            return
                "PROGRAM \"Rotating Group Circuit\"\n"
                + "MODE CIRCUIT\n\n"
                + "REPEAT 4\n"
                + "    WORK 45\n"
                + "    REST 15\n"
                + "END\n\n"
                + "BETWEEN\n"
                + "    MOVE 20 \"Move clockwise to the next station\"\n"
                + "END BETWEEN\n\n"
                + "STATION \"Station 1\"\nCOLOUR #E53935\nEND STATION\n\n"
                + "STATION \"Station 2\"\nCOLOUR #FB8C00\nEND STATION\n\n"
                + "STATION \"Station 3\"\nCOLOUR #FDD835\nEND STATION\n\n"
                + "STATION \"Station 4\"\nCOLOUR #43A047\nEND STATION\n\n"
                + "STATION \"Station 5\"\nCOLOUR #1E88E5\nEND STATION\n\n"
                + "STATION \"Station 6\"\nCOLOUR #3949AB\nEND STATION\n\n"
                + "STATION \"Station 7\"\nCOLOUR #8E24AA\nEND STATION\n\n"
                + "STATION \"Station 8\"\nCOLOUR #D81B60\nEND STATION";
        }

        private sealed class FakeAiProgramService : IAiProgramService
        {
            private readonly Queue<string> _responses;
            private readonly int _delayMs;

            public List<IReadOnlyList<AiChatMessage>> Calls { get; } = new List<IReadOnlyList<AiChatMessage>>();
            public int CallCount { get { return Calls.Count; } }
            public int RemainingResponses { get { return _responses.Count; } }

            public FakeAiProgramService(IEnumerable<string> responses, int delayMs = 0)
            {
                _responses = new Queue<string>(responses ?? Array.Empty<string>());
                _delayMs = delayMs;
            }

            public async Task<string> GenerateTextAsync(IReadOnlyList<AiChatMessage> messages, CancellationToken cancellationToken = default)
            {
                Calls.Add(messages == null ? Array.Empty<AiChatMessage>() : messages.ToArray());

                if (_delayMs > 0)
                {
                    await Task.Delay(_delayMs, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (_responses.Count == 0) return string.Empty;
                return _responses.Dequeue();
            }
        }
    }
}
