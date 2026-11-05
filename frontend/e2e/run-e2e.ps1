<#
.SYNOPSIS
  Runs the Playwright suite with the persona logins read from a file outside the repo.

.DESCRIPTION
  The logins live in your user folder, never in the repo (it is public):
    local run  ->  ~\.ppdo\e2e-local.env
    UAT run    ->  ~\.ppdo\e2e-uat.env
  One KEY=VALUE per line, E2E_* keys only (see the template this script writes the first time).
  To move to another laptop, copy the ~\.ppdo folder.

  The script:
    - sets the E2E_* variables for this run only, and puts your shell back as it was afterwards;
    - refuses to run against production;
    - takes the shared e2e lock (one run at a time on this machine) and releases it;
    - after a UAT run, deletes the saved UAT sign-ins in e2e\.auth and runs with traces off.
  It never prints a password.

.EXAMPLE
  .\e2e\run-e2e.ps1                              # local, whole suite
  .\e2e\run-e2e.ps1 -Target uat                  # UAT, whole suite
  .\e2e\run-e2e.ps1 -Target uat sweep.spec.ts    # UAT, one spec (anything after -Target goes to Playwright)
#>
# No positional binding: without it, "sweep.spec.ts" after -Target lands in -EnvFile (PPDO-303).
[CmdletBinding(PositionalBinding = $false)]
param(
  [ValidateSet("local", "uat")]
  [string]$Target = "local",
  [string]$EnvFile,
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$PlaywrightArgs
)

$ErrorActionPreference = "Stop"

# Production hosts (CLAUDE.md, Production Deployment). A run against these is refused, whatever the target.
$ProductionHosts = @("jolly-sky-0e3a2e310", "ppdo-portal-api-sea")

$frontend = Split-Path $PSScriptRoot -Parent
if (-not $EnvFile) { $EnvFile = Join-Path $HOME ".ppdo\e2e-$Target.env" }

# --- 1. The env file: write a template the first time ------------------------------------------
if (-not (Test-Path $EnvFile)) {
  New-Item -ItemType Directory -Force (Split-Path $EnvFile -Parent) | Out-Null
  $template = @(
    "# Persona logins for the $Target Playwright run. Keep this file OUT of the repo."
    "# One KEY=VALUE per line. A persona with an empty email or password is skipped."
    "# *_EMAIL is what you type in the login form's Username box."
    ""
  )
  if ($Target -eq "uat") {
    $template += @(
      "E2E_BASE_URL=https://<uat-frontend-host>"
      "E2E_API_URL=https://<uat-function-app-host>/api"
      "# Takes the E2E office back out of the AIP when the run ends (README, AIP journeys)."
      "E2E_AIP_CLEANUP=1"
      ""
    )
  }
  $template += @(
    "# Seed Admin for the journey specs (not a persona)"
    "E2E_SEED_EMAIL="
    "E2E_SEED_PASSWORD="
    ""
  )
  foreach ($k in "SA", "AD", "RV", "ST", "EN", "XD") {
    $template += "E2E_${k}_EMAIL="
    $template += "E2E_${k}_PASSWORD="
  }
  Set-Content -Path $EnvFile -Value $template -Encoding UTF8
  Write-Host "Created $EnvFile. Fill in the logins, then run this again."
  exit 1
}

# --- 2. Read it -------------------------------------------------------------------------------------
$values = [ordered]@{}
$lineNo = 0
foreach ($line in Get-Content -Path $EnvFile -Encoding UTF8) {
  $lineNo++
  $t = $line.Trim()
  if ($t -eq "" -or $t.StartsWith("#")) { continue }
  $eq = $t.IndexOf("=")
  if ($eq -lt 1) { throw "$EnvFile line ${lineNo}: expected KEY=VALUE." }
  $key = $t.Substring(0, $eq).Trim()
  $val = $t.Substring($eq + 1).Trim()
  if ($val.Length -ge 2 -and (($val.StartsWith('"') -and $val.EndsWith('"')) -or ($val.StartsWith("'") -and $val.EndsWith("'")))) {
    $val = $val.Substring(1, $val.Length - 2)
  }
  # Only E2E_* keys, so the file can't change PATH, NODE_OPTIONS or anything else in the run.
  if ($key -notmatch '^E2E_[A-Z0-9_]+$') { throw "$EnvFile line ${lineNo}: only E2E_* keys are allowed ('$key')." }
  if ($val -ne "") { $values[$key] = $val }
}

# --- 3. Where it points -----------------------------------------------------------------------------
foreach ($k in "E2E_BASE_URL", "E2E_API_URL") {
  if (-not $values.Contains($k)) { continue }
  foreach ($h in $ProductionHosts) {
    if ($values[$k] -like "*$h*") { throw "$k points at production ($h). Playwright never runs against production." }
  }
  if ($values[$k] -like "*<*") { throw "$k in $EnvFile is still the template placeholder." }
}
$baseUrl = if ($values.Contains("E2E_BASE_URL")) { $values["E2E_BASE_URL"] } else { "" }
$isLocalUrl = ($baseUrl -eq "") -or ($baseUrl -match '^https?://(localhost|127\.0\.0\.1|\[::1\])(:\d+)?(/|$)')
if ($Target -eq "uat" -and $isLocalUrl) { throw "Target is uat, but E2E_BASE_URL in $EnvFile is empty or local." }
if ($Target -eq "local" -and -not $isLocalUrl) { throw "Target is local, but E2E_BASE_URL in $EnvFile is $baseUrl. Use -Target uat." }

$personas = @("SA", "AD", "RV", "ST", "EN", "XD") | Where-Object {
  $values.Contains("E2E_${_}_EMAIL") -and $values.Contains("E2E_${_}_PASSWORD")
}
if (-not $personas) { throw "No persona in $EnvFile has both an email and a password." }

# --- 4. The lock: one run at a time on this machine ------------------------------------------------
# Next to the main checkout (worktrees included), the same place the Claude sessions use.
$commonDir = (& git -C $frontend rev-parse --path-format=absolute --git-common-dir).Trim()
$lockName = if ($Target -eq "uat") { ".e2e-lock-uat" } else { ".e2e-lock" }
$lock = Join-Path (Split-Path (Split-Path $commonDir -Parent) -Parent) $lockName
try { New-Item -ItemType Directory -Path $lock -ErrorAction Stop | Out-Null }
catch { throw "Another $Target run holds the lock ($lock). Wait for it to finish. If none is running, delete that folder." }

# --- 5. Run, then put everything back ---------------------------------------------------------------
$saved = @{}
Get-ChildItem Env: | Where-Object { $_.Name -like "E2E_*" } | ForEach-Object { $saved[$_.Name] = $_.Value }
$exitCode = 1
try {
  foreach ($name in @($saved.Keys)) { Remove-Item "Env:$name" }
  foreach ($k in $values.Keys) { Set-Item "Env:$k" $values[$k] }

  $where = if ($Target -eq "uat") { $baseUrl } else { "local" }
  Write-Host "Playwright ($where) as $($personas -join ', ')$(if ($values.Contains('E2E_SEED_EMAIL')) { ' + seed' })"

  $npmArgs = @("run", $(if ($Target -eq "uat") { "e2e:uat" } else { "e2e" }), "--")
  # A trace records what was typed, the password included. Keep none from UAT.
  if ($Target -eq "uat") { $npmArgs += @("--trace", "off") }
  if ($PlaywrightArgs) { $npmArgs += $PlaywrightArgs }

  Push-Location $frontend
  try { & npm.cmd @npmArgs; $exitCode = $LASTEXITCODE }
  finally { Pop-Location }
}
finally {
  Get-ChildItem Env: | Where-Object { $_.Name -like "E2E_*" } | ForEach-Object { Remove-Item "Env:$($_.Name)" }
  foreach ($name in $saved.Keys) { Set-Item "Env:$name" $saved[$name] }
  # The saved sign-ins are live UAT sessions. Don't leave them on disk.
  if ($Target -eq "uat") { Remove-Item (Join-Path $frontend "e2e\.auth\*.json") -ErrorAction SilentlyContinue }
  Remove-Item $lock -Force -ErrorAction SilentlyContinue
}
exit $exitCode
