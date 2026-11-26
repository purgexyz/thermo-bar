param(
    # Small exe (~4 MB) that needs the .NET 8 Desktop Runtime. Pass -SelfContained for a ~68 MB exe with no prerequisites.
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$sc = if ($SelfContained) { 'true' } else { 'false' }

dotnet publish "$PSScriptRoot\src\TempBar\TempBar.csproj" `
    -c Release -r win-x64 --self-contained $sc `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=$sc `
    -p:DebugType=none `
    -o "$PSScriptRoot\dist"

Write-Host "Built $PSScriptRoot\dist\TempBar.exe"
