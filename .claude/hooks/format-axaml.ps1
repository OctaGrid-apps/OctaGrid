# PostToolUse (Edit|Write): format edited .axaml files with xstyler, as AGENTS.md requires.
$ErrorActionPreference = 'Stop'
$data = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = $data.tool_input.file_path
if (-not $path -or $path -notlike '*.axaml' -or -not (Test-Path -LiteralPath $path)) { exit 0 }

Push-Location $env:CLAUDE_PROJECT_DIR
try {
    $out = & dotnet xstyler -f $path 2>&1
    if ($LASTEXITCODE -ne 0) {
        # Exit 2 on PostToolUse shows stderr to the model; the edit itself already happened.
        [Console]::Error.WriteLine("xstyler failed on ${path}:`n$($out -join "`n")")
        exit 2
    }
} finally { Pop-Location }
exit 0
