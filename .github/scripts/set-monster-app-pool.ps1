[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("StartAppPool", "StopAppPool")]
    [string]$Mode,

    [Parameter(Mandatory)]
    [string]$WebsiteName,

    [Parameter(Mandatory)]
    [string]$ServerComputerName,

    [Parameter(Mandatory)]
    [string]$ServerUsername,

    [Parameter(Mandatory)]
    [string]$ServerPassword
)

$ErrorActionPreference = "Stop"
$msdeploy = "C:\Program Files (x86)\IIS\Microsoft Web Deploy V3\msdeploy.exe"
if (-not (Test-Path -LiteralPath $msdeploy -PathType Leaf)) {
    throw "Microsoft Web Deploy V3 is unavailable on this runner."
}

$computerName = "$ServerComputerName/MsDeploy.axd?site=$WebsiteName"
$arguments = @(
    "-verb:sync"
    "-allowUntrusted"
    "-source:recycleApp"
    ("-dest:recycleApp={0},recycleMode={1},computerName={2},username={3},password={4},AuthType='Basic'" -f
        $WebsiteName,
        $Mode,
        $computerName,
        $ServerUsername,
        $ServerPassword)
)

& $msdeploy $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Microsoft Web Deploy could not set application pool '$WebsiteName' to '$Mode'."
}
