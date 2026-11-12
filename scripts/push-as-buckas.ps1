param(
    [Parameter(Mandatory = $true)]
    [string]$BuckasEmail,

    [string]$CommitMessage = "Fix AI assistant validation/clarification loops and script hardening",
    [string]$Branch = ""
)

$ErrorActionPreference = "Stop"

Write-Host "== Configure repo identity (local only) =="
git rev-parse --is-inside-work-tree | Out-Null

git config user.name "Buckas"
git config user.email $BuckasEmail

Write-Host "Using identity:"
Write-Host "  Name : $(git config --get user.name)"
Write-Host "  Email: $(git config --get user.email)"

if ([string]::IsNullOrWhiteSpace($Branch)) {
    $Branch = (git rev-parse --abbrev-ref HEAD).Trim()
}

Write-Host "== Optional: re-auth GitHub CLI as Buckas =="
Write-Host "If needed, run manually: gh auth logout ; gh auth login"

Write-Host "== Commit and push =="
git add -A

$staged = git diff --cached --name-only
if (-not $staged) {
    Write-Host "No staged changes to commit."
} else {
    git commit -m $CommitMessage
}

git push -u origin $Branch

Write-Host "Done. Pushed branch '$Branch' as local identity Buckas."
Write-Host "If push still uses old credentials, remove github.com credentials from Windows Credential Manager and push again."
