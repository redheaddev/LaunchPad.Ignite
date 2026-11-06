[CmdletBinding()]
param(
	[string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $repoRoot
$extensionRoot = Join-Path $workspaceRoot "LaunchPad.Ignite.Extension.Windows"
$projectPath = Join-Path $extensionRoot "LaunchPad.Ignite.Extension.Windows.csproj"
$manifestPath = Join-Path $extensionRoot "extension.json"

if (-not (Test-Path -LiteralPath $projectPath)) {
	throw "Windows extension project not found: $projectPath"
}
if (-not (Test-Path -LiteralPath $manifestPath)) {
	throw "Extension manifest not found: $manifestPath"
}

$manifestText = [System.IO.File]::ReadAllText($manifestPath)
$manifest = $manifestText | ConvertFrom-Json
if ($manifest.version -notmatch '^\d+\.\d+\.\d+$') {
	throw "Extension manifest version must use numeric major.minor.patch format."
}
if ($manifest.id -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
	throw "Extension ID '$($manifest.id)' is not valid for a package filename."
}

$currentVersion = [version]::Parse($manifest.version)
if ([string]::IsNullOrWhiteSpace($Version)) {
	$Version = "{0}.{1}.{2}" -f $currentVersion.Major, $currentVersion.Minor, ($currentVersion.Build + 1)
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
	throw "Version must use numeric major.minor.patch format, such as 1.2.3."
}
if ([version]::Parse($Version) -le $currentVersion) {
	throw "Version '$Version' must be greater than the current extension version '$currentVersion'."
}

function Set-ManifestVersionText {
	param(
		[string]$Content,
		[string]$TargetVersion
	)

	$pattern = '(?m)^(?<prefix>[ \t]*"version"[ \t]*:[ \t]*)"[^"]+"'
	$matches = [System.Text.RegularExpressions.Regex]::Matches($Content, $pattern)
	if ($matches.Count -ne 1) {
		throw "Expected exactly one version property in the extension manifest."
	}

	$match = $matches[0]
	$replacement = $match.Groups["prefix"].Value + '"' + $TargetVersion + '"'
	return $Content.Substring(0, $match.Index) + $replacement + $Content.Substring($match.Index + $match.Length)
}

$temporaryRoot = Join-Path $env:TEMP "LaunchPad.ExtensionPublish.$([guid]::NewGuid().ToString('N'))"
$publishDirectory = Join-Path $temporaryRoot "publish"
$archivePath = $null

try {
	New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
	& dotnet publish $projectPath `
		--configuration Release `
		--output $publishDirectory `
		"-p:Version=$Version" `
		"-p:IncludeSourceRevisionInInformationalVersion=false"
	if ($LASTEXITCODE -ne 0) {
		throw "dotnet publish failed for the Windows extension."
	}

	$publishedManifestPath = Join-Path $publishDirectory "extension.json"
	if (-not (Test-Path -LiteralPath $publishedManifestPath)) {
		throw "Published extension manifest not found: $publishedManifestPath"
	}
	$publishedManifestText = [System.IO.File]::ReadAllText($publishedManifestPath)
	$publishedManifest = $publishedManifestText | ConvertFrom-Json
	if ([string]::IsNullOrWhiteSpace($publishedManifest.assembly)) {
		throw "Extension manifest does not declare an assembly."
	}
	$assemblyPath = Join-Path $publishDirectory $publishedManifest.assembly
	if (-not (Test-Path -LiteralPath $assemblyPath)) {
		throw "Published extension assembly not found: $assemblyPath"
	}

	$updatedPublishedManifest = Set-ManifestVersionText -Content $publishedManifestText -TargetVersion $Version
	[System.IO.File]::WriteAllText(
		$publishedManifestPath,
		$updatedPublishedManifest,
		[System.Text.UTF8Encoding]::new($false))

	$packageName = "$($manifest.id)-$Version.zip"
	$artifactDirectory = Join-Path $extensionRoot "artifacts\packages"
	New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
	$archivePath = Join-Path $artifactDirectory $packageName
	Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archivePath -Force

	Add-Type -AssemblyName System.IO.Compression.FileSystem
	$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
	try {
		$entryNames = $archive.Entries.FullName
		if ($entryNames -notcontains "extension.json" -or $entryNames -notcontains $publishedManifest.assembly) {
			throw "Package ZIP must contain extension.json and its assembly at the archive root."
		}
	}
	finally {
		$archive.Dispose()
	}

	$packageDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "LaunchPad.Ignite\packages"
	New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
	$profilePackagePath = Join-Path $packageDirectory $packageName
	Copy-Item -LiteralPath $archivePath -Destination $profilePackagePath -Force

	$updatedManifest = Set-ManifestVersionText -Content $manifestText -TargetVersion $Version
	[System.IO.File]::WriteAllText(
		$manifestPath,
		$updatedManifest,
		[System.Text.UTF8Encoding]::new($false))

	Write-Host "Published extension package: $archivePath"
	Write-Host "Copied extension package to: $profilePackagePath"
	Write-Host "Install with: extension install $($manifest.id)"
}
finally {
	if (Test-Path -LiteralPath $temporaryRoot) {
		Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
	}
}