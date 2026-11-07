# PreToolUse (Edit|Write): package versions and version.json change only in a release change.
# A release change = branch name containing "release", or OCTAGRID_RELEASE=1 in the environment.
$data = [Console]::In.ReadToEnd() | ConvertFrom-Json
$ti = $data.tool_input
$path = [string]$ti.file_path
$leaf = Split-Path -Leaf $path
if ($leaf -ne 'version.json' -and $leaf -ne 'Directory.Packages.props') { exit 0 }

function Deny($msg) { [Console]::Error.WriteLine($msg); exit 2 }

if ($env:OCTAGRID_RELEASE -eq '1') { exit 0 }
$branch = (& git -C $env:CLAUDE_PROJECT_DIR rev-parse --abbrev-ref HEAD 2>$null)
if ($branch -match 'release') { exit 0 }

if ($leaf -eq 'version.json') {
    Deny "Blocked: version.json is edited only in a release change (branch name containing 'release'). Ask the user first."
}

# Directory.Packages.props: adding a new PackageVersion is fine, changing an existing version is not.
if (-not (Test-Path -LiteralPath $path)) { exit 0 }
$old = [IO.File]::ReadAllText($path)
if ($data.tool_name -eq 'Write') {
    $new = [string]$ti.content
} else {
    $idx = $old.IndexOf([string]$ti.old_string, [StringComparison]::Ordinal)
    if ($idx -lt 0) { exit 0 }   # the edit will fail on its own
    if ($ti.replace_all) { $new = $old.Replace([string]$ti.old_string, [string]$ti.new_string) }
    else { $new = $old.Substring(0, $idx) + $ti.new_string + $old.Substring($idx + ([string]$ti.old_string).Length) }
}

function Get-Versions($text) {
    $map = @{}
    foreach ($m in [regex]::Matches($text, '<PackageVersion\s+Include="([^"]+)"\s+Version="([^"]+)"')) { $map[$m.Groups[1].Value] = $m.Groups[2].Value }
    $map
}
$before = Get-Versions $old
$after = Get-Versions $new
$changed = @($before.Keys | Where-Object { $after.ContainsKey($_) -and $after[$_] -ne $before[$_] })
if ($changed.Count -gt 0) {
    Deny "Blocked: package versions ($($changed -join ', ')) change only in a release change (branch name containing 'release'). Ask the user first."
}
exit 0
