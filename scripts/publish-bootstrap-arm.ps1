param(
	[ValidateSet('linux-arm','linux-arm64')]
	[string]$Runtime = 'linux-arm64',

	[ValidateSet('Debug','Release')]
	[string]$Configuration = 'Release',

	[switch]$SelfContained
)

$ErrorActionPreference = 'Stop'

$selfContainedArg = if ($SelfContained.IsPresent) { 'true' } else { 'false' }
$outDir = Join-Path 'artifacts' ("bootstrap-$Runtime-$Configuration" + (if ($SelfContained) { '-sc' } else { '-fd' }))

Write-Host "Publishing MeshWave.Bootstrap for $Runtime ($Configuration, self-contained=$selfContainedArg)..."

dotnet publish MeshWave.Bootstrap/MeshWave.Bootstrap.csproj `
	-c $Configuration `
	-r $Runtime `
	--self-contained $selfContainedArg `
	-o $outDir

Write-Host "Done. Output: $outDir"
