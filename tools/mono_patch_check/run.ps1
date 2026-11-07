# Applies every Harmony patch of the built mod inside the game's own Mono runtime, without opening the game.
# Run it before installing a new version: a patch that Mono can't handle closes the game while a park loads,
# and only this runtime reproduces that. It doesn't exercise the patches, so it can't tell whether a move works.
param(
    [string]$GameDir = "D:\games\steam\steamapps\common\Parkitect",
    [string]$ModDll = (Join-Path $PSScriptRoot "..\..\bin\Release\net48\move_it.dll")
)

$ErrorActionPreference = "Stop"
$ModDll = (Resolve-Path $ModDll).Path
$HostProject = Join-Path $PSScriptRoot "host\mono_host.csproj"
$CheckProject = Join-Path $PSScriptRoot "check\patch_check.csproj"

foreach ($Project in $HostProject, $CheckProject) {
    dotnet build $Project -c Release -nologo -v quiet
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$HostExe = Join-Path $PSScriptRoot "host\bin\Release\net10.0\mono_host.exe"
$CheckExe = Join-Path $PSScriptRoot "check\bin\Release\net48\patch_check.exe"
$Managed = Join-Path $GameDir "Parkitect_Data\Managed"
$Log = Join-Path $PSScriptRoot "last_run.txt"

# Mono takes its paths with forward slashes here. The last line of the log is ALL OK when everything applied.
& $HostExe $GameDir.Replace('\', '/') $CheckExe.Replace('\', '/') $Managed.Replace('\', '/') $ModDll.Replace('\', '/') $Log.Replace('\', '/')
exit $LASTEXITCODE
