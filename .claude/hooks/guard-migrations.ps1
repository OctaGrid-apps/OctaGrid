# PreToolUse (Edit|Write|Bash): pre-alpha DB policy - only the single InitialCreate migration
# (and the model snapshot) may be created or changed; never add further migrations.
$data = [Console]::In.ReadToEnd() | ConvertFrom-Json
$ti = $data.tool_input

function Deny($msg) { [Console]::Error.WriteLine($msg); exit 2 }

if ($data.tool_name -eq 'Bash') {
    if ($ti.command -match 'migrations\s+add\s+(?<name>\S+)' -and $Matches['name'] -notmatch '^["'']?InitialCreate["'']?$') {
        Deny "Blocked: pre-alpha DB policy forbids new EF migrations. Delete and regenerate the single 'InitialCreate' migration and snapshot instead (see AGENTS.md)."
    }
    exit 0
}

$path = ([string]$ti.file_path) -replace '\\', '/'
if ($path -notmatch '/Migrations/') { exit 0 }
$leaf = Split-Path -Leaf $path
if ($leaf -match '_InitialCreate(\.Designer)?\.cs$' -or $leaf -match 'ModelSnapshot\.cs$') { exit 0 }
Deny "Blocked: '$leaf' is not part of the InitialCreate migration. Pre-alpha DB policy: do not add migrations; regenerate InitialCreate and the snapshot with 'dotnet ef migrations' (see AGENTS.md)."
