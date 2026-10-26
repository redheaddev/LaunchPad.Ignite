[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
	[string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = & git -C $PSScriptRoot rev-parse --show-toplevel
if ($LASTEXITCODE -ne 0) {
	throw "Could not find the Git repository from '$PSScriptRoot'."
}
$repoRoot = ($repoRoot | Select-Object -First 1).Trim()

function Invoke-Git {
	param(
		[string[]]$GitArguments
	)

	$output = & git -C $repoRoot @GitArguments
	if ($LASTEXITCODE -ne 0) {
		throw "git $($GitArguments -join ' ') failed."
	}
	return $output
}

$projectRelativePath = "LaunchPad.Ignite.App/LaunchPad.Ignite.App.csproj"
$projectPath = Join-Path $repoRoot $projectRelativePath
if (-not (Test-Path -LiteralPath $projectPath)) {
	throw "App project not found: $projectPath"
}

$branch = (Invoke-Git -GitArguments @("branch", "--show-current") | Select-Object -First 1).Trim()
if (-not $branch) {
	throw "Release must be run from a named branch, not a detached HEAD."
}

$status = @(Invoke-Git -GitArguments @("status", "--porcelain"))
if ($status.Count -gt 0) {
	throw "Working tree is not clean. Commit, stash, or discard changes before releasing."
}

$null = Invoke-Git -GitArguments @("remote", "get-url", "origin")

$projectContent = [System.IO.File]::ReadAllText($projectPath)
$versionPattern = '(<Version>\s*)(\d+\.\d+\.\d+)(\s*</Version>)'
$versionMatch = [regex]::Match($projectContent, $versionPattern)
if (-not $versionMatch.Success) {
	throw "Could not find a numeric major.minor.patch <Version> in '$projectRelativePath'."
}

$currentVersion = [version]::Parse($versionMatch.Groups[2].Value)
if ([string]::IsNullOrWhiteSpace($Version)) {
	$Version = "{0}.{1}.{2}" -f $currentVersion.Major, $currentVersion.Minor, ($currentVersion.Build + 1)
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
	throw "Version must use numeric major.minor.patch format, such as 1.2.3."
}

$targetVersion = [version]::Parse($Version)
if ($targetVersion -le $currentVersion) {
	throw "Version '$Version' must be greater than the current version '$currentVersion'."
}

$tag = "v$Version"
if (@(Invoke-Git -GitArguments @("tag", "--list", $tag)) -contains $tag) {
	throw "Tag '$tag' already exists locally."
}
if (@(Invoke-Git -GitArguments @("ls-remote", "--tags", "origin", "refs/tags/$tag")).Count -gt 0) {
	throw "Tag '$tag' already exists on origin."
}

$updatedProjectContent = $projectContent.Substring(0, $versionMatch.Index) +
	$versionMatch.Groups[1].Value + $Version + $versionMatch.Groups[3].Value +
	$projectContent.Substring($versionMatch.Index + $versionMatch.Length)
if ($PSCmdlet.ShouldProcess("origin/$branch and $tag", "Bump version to $Version, commit, tag, and push")) {
	[System.IO.File]::WriteAllText($projectPath, $updatedProjectContent, [System.Text.UTF8Encoding]::new($false))
	Invoke-Git -GitArguments @("add", "--", $projectRelativePath) | Out-Null
	Invoke-Git -GitArguments @("commit", "-m", "chore: release $tag") | Out-Null
	Invoke-Git -GitArguments @("tag", "--annotate", $tag, "--message", "LaunchPad Ignite $tag") | Out-Null
	Invoke-Git -GitArguments @("push", "--atomic", "origin", "HEAD:refs/heads/$branch", "refs/tags/$tag")
	Write-Host "Pushed $tag from branch '$branch'. The GitHub Actions workflow will build and publish the release." -ForegroundColor Green
}
