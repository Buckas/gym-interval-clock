using System;

namespace GymClock.AI
{
    public enum ProposalValidationState
    {
        NotValidated,
        Valid,
        Invalid
    }

    public sealed class ProgramAssistantSessionState
    {
        private string _validatedScriptSnapshot = string.Empty;

        public string CurrentProposalScript { get; private set; } = string.Empty;
        public AiProgramProposal CurrentProposal { get; private set; }
        public ProgramValidationResult CurrentValidation { get; private set; }
        public ProposalValidationState ValidationState { get; private set; } = ProposalValidationState.NotValidated;

        public bool CanApply
        {
            get
            {
                return ValidationState == ProposalValidationState.Valid
                    && CurrentValidation != null
                    && !string.IsNullOrWhiteSpace(CurrentProposalScript)
                    && string.Equals(CurrentProposalScript, _validatedScriptSnapshot, StringComparison.Ordinal);
            }
        }

        public void SetGeneratedProposal(AiProgramProposal proposal)
        {
            CurrentProposal = proposal;
            CurrentProposalScript = proposal?.Script ?? string.Empty;

            if (proposal == null)
            {
                CurrentValidation = null;
                _validatedScriptSnapshot = string.Empty;
                ValidationState = ProposalValidationState.NotValidated;
                return;
            }

            if (proposal.IsValid)
            {
                _validatedScriptSnapshot = CurrentProposalScript;
                ValidationState = ProposalValidationState.Valid;
                CurrentValidation = new ProgramValidationResult
                {
                    Script = proposal.Script,
                    ParsedProgram = proposal.ParsedProgram,
                    Issues = proposal.Issues
                };
            }
            else
            {
                _validatedScriptSnapshot = string.Empty;
                ValidationState = ProposalValidationState.Invalid;
                CurrentValidation = new ProgramValidationResult
                {
                    Script = proposal.Script,
                    ParsedProgram = proposal.ParsedProgram,
                    Issues = proposal.Issues
                };
            }
        }

        public void SetProposalTextEditedByUser(string editedScript)
        {
            CurrentProposalScript = editedScript ?? string.Empty;
            CurrentValidation = null;
            ValidationState = ProposalValidationState.NotValidated;
        }

        public void SetValidationResult(ProgramValidationResult validation)
        {
            CurrentValidation = validation;
            CurrentProposalScript = validation?.Script ?? CurrentProposalScript;

            if (validation != null && validation.IsValid)
            {
                _validatedScriptSnapshot = CurrentProposalScript;
                ValidationState = ProposalValidationState.Valid;
            }
            else
            {
                _validatedScriptSnapshot = string.Empty;
                ValidationState = ProposalValidationState.Invalid;
            }
        }

        public void Clear()
        {
            CurrentProposal = null;
            CurrentValidation = null;
            CurrentProposalScript = string.Empty;
            _validatedScriptSnapshot = string.Empty;
            ValidationState = ProposalValidationState.NotValidated;
        }
    }
}
