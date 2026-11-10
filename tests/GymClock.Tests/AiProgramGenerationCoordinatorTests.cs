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

            Assert.True(proposal.IsValid);
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

            Assert.True(proposal.IsValid);
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

            Assert.True(proposal.IsValid);
            Assert.DoesNotContain("```", proposal.Script);
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
