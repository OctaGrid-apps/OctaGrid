# Stop: quiet build of the solution; if it reports warnings or errors, hand them back once so they get fixed.
$data = [Console]::In.ReadToEnd() | ConvertFrom-Json
if ($data.stop_hook_active) { exit 0 }   # already re-prompted once; never loop

Push-Location $env:CLAUDE_PROJECT_DIR
try {
    $changed = & git status --porcelain 2>$null | Where-Object { $_ -match '\.(cs|axaml|csproj|props|targets|slnx|editorconfig)\s*$' }
    if (-not $changed) { exit 0 }   # nothing buildable changed

    $out = & dotnet build OctaGrid.slnx -nologo -v:q -clp:NoSummary 2>&1 | ForEach-Object { "$_" }
    $issues = $out | Where-Object { $_ -match ': (warning|error) ' } | Sort-Object -Unique
    if ($issues) {
        [Console]::Error.WriteLine("dotnet build OctaGrid.slnx reported problems (the build must stay warning-free). Fix them or explain why not:`n" + ($issues -join "`n"))
        exit 2
    }
} finally { Pop-Location }
exit 0
