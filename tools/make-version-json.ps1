# WARNING: this script is only for the old Java 8.x line. It rewrites the WHOLE version.json
# and therefore deletes the "platforms" section (macOS / Linux / per-platform entries).
# For anything newer use:  dotnet run --project csharp\tools\ReleaseTool -- merge ...
# (see csharp\RELEASING.md)
#
# Writes version.json for the app's update check / auto-update.
# Called by release.bat after the installer is built (and signed).
#
#   -Version    e.g. 8.1.0           (AppVersion from target\Atbash.iss)
#   -Installer  path to Setup_x.y.z.exe
#   -Out        version.json to write (the "notes" text already in it is kept)
#
# The installer URL points to the GitHub Release asset, so upload the exe to a
# release tagged v<Version> BEFORE pushing the new version.json.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Installer,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Repo = "KoreaJava-53bulgogi/AtbashCipherTool"
)
$ErrorActionPreference = "Stop"

$utf8 = New-Object System.Text.UTF8Encoding($false)   # UTF-8 without BOM

# Keep the existing notes (release notes are edited by hand in version.json)
$notes = ""
if (Test-Path $Out) {
    $old = [System.IO.File]::ReadAllText((Resolve-Path $Out), $utf8)
    $m = [regex]::Match($old, '"notes"\s*:\s*"((?:[^"\\]|\\.)*)"')
    if ($m.Success) { $notes = $m.Groups[1].Value }   # already JSON-escaped
}

# SHA-256 via .NET directly (Get-FileHash is missing when Windows PowerShell is
# started from PowerShell 7 because of a mixed-up module path)
$stream = [System.IO.File]::OpenRead((Resolve-Path -LiteralPath $Installer))
try {
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    $sha = ([System.BitConverter]::ToString($hasher.ComputeHash($stream)) -replace '-', '').ToLower()
} finally {
    $stream.Dispose()
}
$file = Split-Path -Leaf $Installer
$installerUrl = "https://github.com/$Repo/releases/download/v$Version/$file"
$pageUrl = "https://github.com/$Repo/releases/latest"

$json = "{`n" +
        "  `"version`": `"$Version`",`n" +
        "  `"url`": `"$pageUrl`",`n" +
        "  `"installer`": `"$installerUrl`",`n" +
        "  `"sha256`": `"$sha`",`n" +
        "  `"notes`": `"$notes`"`n" +
        "}`n"
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($Out), $json, $utf8)

Write-Host "version.json: $Version"
Write-Host "  installer: $installerUrl"
Write-Host "  sha256:    $sha"
