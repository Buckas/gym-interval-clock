using System.Linq;
using GymClock.AI;
using Xunit;

namespace GymClock.Tests
{
    public class ProgramAssistantSessionStateTests
    {
        [Fact]
        public void ValidGeneratedProposal_EnablesApply()
        {
            ProgramAssistantSessionState state = new ProgramAssistantSessionState();
            AiProgramProposal proposal = new AiProgramProposal
            {
                UserRequest = "Create",
                Script = "PROGRAM \"P\"\nMODE SHARED\nWORK 30",
                ParsedProgram = new WorkoutProgram(),
                Issues = new ProgramValidationIssue[0]
            };

            state.SetGeneratedProposal(proposal);

            Assert.Equal(ProposalValidationState.Valid, state.ValidationState);
            Assert.True(state.CanApply);
        }

        [Fact]
        public void ManualEdit_InvalidatesValidation_AndDisablesApply()
        {
            ProgramAssistantSessionState state = new ProgramAssistantSessionState();
            AiProgramProposal proposal = new AiProgramProposal
            {
                UserRequest = "Create",
                Script = "PROGRAM \"P\"\nMODE SHARED\nWORK 30",
                ParsedProgram = new WorkoutProgram(),
                Issues = new ProgramValidationIssue[0]
            };
            state.SetGeneratedProposal(proposal);

            state.SetProposalTextEditedByUser("PROGRAM \"Changed\"");

            Assert.Equal(ProposalValidationState.NotValidated, state.ValidationState);
            Assert.False(state.CanApply);
        }

        [Fact]
        public void RevalidateValidProposal_RestoresApply()
        {
            ProgramAssistantSessionState state = new ProgramAssistantSessionState();
            AiProgramProposal proposal = new AiProgramProposal
            {
                UserRequest = "Create",
                Script = "PROGRAM \"P\"\nMODE SHARED\nWORK 30",
                ParsedProgram = new WorkoutProgram(),
                Issues = new ProgramValidationIssue[0]
            };
            state.SetGeneratedProposal(proposal);
            state.SetProposalTextEditedByUser("PROGRAM \"Changed\"");

            ProgramValidationResult validation = new ProgramValidationResult
            {
                Script = "PROGRAM \"Changed\"",
                ParsedProgram = new WorkoutProgram(),
                Issues = new ProgramValidationIssue[0]
            };
            state.SetValidationResult(validation);

            Assert.Equal(ProposalValidationState.Valid, state.ValidationState);
            Assert.True(state.CanApply);
        }

        [Fact]
        public void InvalidValidation_DisablesApply()
        {
            ProgramAssistantSessionState state = new ProgramAssistantSessionState();
            ProgramValidationResult validation = new ProgramValidationResult
            {
                Script = "PROGRAM \"Broken\"",
                ParsedProgram = null,
                Issues = new[] { new ProgramValidationIssue(ValidationSeverity.Error, 1, "Missing MODE") }
            };

            state.SetValidationResult(validation);

            Assert.Equal(ProposalValidationState.Invalid, state.ValidationState);
            Assert.False(state.CanApply);
            Assert.Contains(state.CurrentValidation.Issues, i => i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void Clear_ResetsState()
        {
            ProgramAssistantSessionState state = new ProgramAssistantSessionState();
            state.SetValidationResult(new ProgramValidationResult
            {
                Script = "PROGRAM \"P\"",
                ParsedProgram = new WorkoutProgram(),
                Issues = new ProgramValidationIssue[0]
            });

            state.Clear();

            Assert.Equal(ProposalValidationState.NotValidated, state.ValidationState);
            Assert.False(state.CanApply);
            Assert.Equal(string.Empty, state.CurrentProposalScript);
            Assert.Null(state.CurrentValidation);
            Assert.Null(state.CurrentProposal);
        }
    }
}
