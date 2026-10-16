[CmdletBinding()]
param(
	[ValidatePattern('^\d+\.\d+\.\d+$')]
	[string]$Version = "1.0.16",

	[string]$Configuration = "Release",

	[ValidateSet("both", "win-x64", "win-arm64")]
	[string]$RuntimeIdentifier = "both"
)

$ErrorActionPreference = "Stop"

function Resolve-Iscc {
	$programFilesX86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
	$candidates = @(
		(Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
		(Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
		(Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
		(Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
		(Join-Path $programFilesX86 "Inno Setup 6\ISCC.exe")
	)

	foreach ($candidate in $candidates) {
		if ($candidate -and (Test-Path $candidate)) {
			return $candidate
		}
	}

	$onPath = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
	if ($null -ne $onPath) {
		return $onPath.Source
	}

	throw "ISCC.exe was not found. Install Inno Setup 6 or 7, or add ISCC.exe to PATH."
}

$installerDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $installerDir
$appProject = Join-Path $repoRoot "LaunchPad.Ignite.App\LaunchPad.Ignite.App.csproj"
$issScript = Join-Path $installerDir "LaunchPad.Ignite.iss"
$outputDir = Join-Path $installerDir "artifacts"

if (-not (Test-Path $appProject)) {
	throw "App project not found: $appProject"
}
if (-not (Test-Path $issScript)) {
	throw "Inno Setup script not found: $issScript"
}

$assemblyVersion = "$Version.0"
$runtimeIdentifiers = if ($RuntimeIdentifier -eq "both") {
	@("win-x64", "win-arm64")
}
else {
	@($RuntimeIdentifier)
}

$iscc = Resolve-Iscc
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

foreach ($rid in $runtimeIdentifiers) {
	$publishDir = Join-Path $outputDir "publish-$rid"
	if (Test-Path $publishDir) {
		Remove-Item -Recurse -Force $publishDir
	}

	& dotnet publish $appProject `
		--configuration $Configuration `
		--runtime $rid `
		--self-contained true `
		--output $publishDir `
		"-p:Version=$Version" `
		"-p:FileVersion=$assemblyVersion" `
		"-p:AssemblyVersion=$assemblyVersion"
	if ($LASTEXITCODE -ne 0) {
		throw "dotnet publish failed for '$rid'."
	}

	$appExecutable = Join-Path $publishDir "LaunchPad.Ignite.App.exe"
	if (-not (Test-Path $appExecutable)) {
		throw "Published app executable not found: $appExecutable"
	}

	$outputName = "LaunchPad.Ignite-Setup_${Version}_${rid}"
	& $iscc "/DAppVersion=$Version" "/DSourceDir=$publishDir" "/DOutputDir=$outputDir" "/DOutputName=$outputName" $issScript
	if ($LASTEXITCODE -ne 0) {
		throw "Inno Setup compilation failed for '$rid'."
	}

	Write-Host "Installer created: $(Join-Path $outputDir ($outputName + '.exe'))" -ForegroundColor Green
}
