# Opens the Git panel on its own, without starting TIA Portal.
# Git commands run for real against the folder you pass. HMI and hardware
# import and export stay unavailable until the panel is opened from TIA Portal.
param(
    [string]$Folder = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($Folder)) {
    $Folder = $root
}

$project = Join-Path $root "src\TiaGitAddIn.Preview\TiaGitAddIn.Preview.csproj"
dotnet run --project $project -p:EnableTiaAddInPackaging=false -p:AllowMissingStartdrive=true -- $Folder
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
