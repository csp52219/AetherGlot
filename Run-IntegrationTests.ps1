<#
.SYNOPSIS
    Runs AetherGlot integration tests with API keys injected from 1Password.

.DESCRIPTION
    Uses `op run` to securely inject API keys defined in integration.op-env
    as environment variables, then runs the xUnit integration tests.

    Prerequisites:
      - 1Password CLI installed: https://developer.1password.com/docs/cli/
      - Signed in: op signin
      - integration.op-env updated to point at your vault items

.PARAMETER Provider
    Limit tests to a specific backend: LiteLLM, Claude, OpenAI, Gemini, or All (default).
    LiteLLM also covers Cloudflare Access when AETHERGLOT_CF_CLIENT_ID / _SECRET are set.

    The same tests run in CI from .github/workflows/integration.yml (GitHub environment
    "integration"); this script is for local runs with 1Password.

.PARAMETER Detailed
    Pass --verbosity detailed to dotnet test.

.EXAMPLE
    .\Run-IntegrationTests.ps1
    .\Run-IntegrationTests.ps1 -Provider LiteLLM
    .\Run-IntegrationTests.ps1 -Provider Claude
    .\Run-IntegrationTests.ps1 -Provider OpenAI -Detailed
#>
[CmdletBinding()]
param(
    [ValidateSet("All", "LiteLLM", "Claude", "OpenAI", "Gemini")]
    [string] $Provider = "All",

    [switch] $Detailed
)

$ErrorActionPreference = "Stop"
$repoRoot  = $PSScriptRoot
$envFile   = Join-Path $repoRoot "integration.op-env"
$testProj  = Join-Path $repoRoot "AetherGlot.Tests\AetherGlot.Tests.csproj"
$verbosity = if ($Detailed) { "detailed" } else { "normal" }

# Verify prerequisites
if (-not (Get-Command op -ErrorAction SilentlyContinue)) {
    Write-Error "1Password CLI (op) not found in PATH. Install from https://developer.1password.com/docs/cli/"
    exit 1
}
if (-not (Test-Path $envFile)) {
    Write-Error "integration.op-env not found at $envFile"
    exit 1
}

# Build the filter expression
$filter = switch ($Provider) {
    "LiteLLM" { "Category=Integration&FullyQualifiedName~LiteLlm" }
    "Claude" { "Category=Integration&FullyQualifiedName~Claude" }
    "OpenAI" { "Category=Integration&FullyQualifiedName~OpenAI" }
    "Gemini" { "Category=Integration&FullyQualifiedName~Gemini" }
    default  { "Category=Integration" }
}

Write-Host "Provider : $Provider"
Write-Host "Filter   : $filter"
Write-Host "Env file : $envFile"
Write-Host ""

# op run injects the op:// references as real env vars for the child process only.
# The key values are never written to disk or printed to the console.
op run --env-file=$envFile -- `
    dotnet test $testProj `
        --filter $filter `
        --verbosity $verbosity
