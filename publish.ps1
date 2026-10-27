param([switch]$DryRun)
$ErrorActionPreference = 'Stop'
$repository = 'https://github.com/elkhantar/quickcrop.git'
$sourceFiles = @('.gitattributes', '.gitignore', 'CHANGELOG.md', 'LICENSE', 'QuickCrop.cs', 'README.md', 'ReviewTests.cs', 'Tests.cs', 'build.ps1', 'test.ps1', 'package.ps1', 'publish.ps1', 'docs')
if ($DryRun) {
    Write-Host "Target: $repository"
    Write-Host 'Stages only source, tests, scripts, docs, and license; commits changes; pushes HEAD to main without force.'
    $sourceFiles | ForEach-Object { Write-Host $_ }
    return
}
Push-Location $PSScriptRoot
try {
    $gitCommon = @('-c', ('safe.directory=' + $PSScriptRoot.Replace('\', '/')))
    function Invoke-RepoGit([string[]]$GitArguments) {
        & git @gitCommon @GitArguments
        if ($LASTEXITCODE -ne 0) { throw "Git failed ($LASTEXITCODE): $($GitArguments -join ' ')" }
    }
    if (-not (Test-Path -LiteralPath "$PSScriptRoot\.git")) { Invoke-RepoGit -GitArguments @('init', '-b', 'main') }
    $remoteNames = & git @gitCommon remote
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect repository remotes.' }
    if ($remoteNames -contains 'origin') {
        $existingRemote = & git @gitCommon remote get-url origin
        if ($LASTEXITCODE -ne 0 -or $existingRemote -ne $repository) { throw "Existing origin differs from $repository; left unchanged." }
    } else { Invoke-RepoGit -GitArguments @('remote', 'add', 'origin', $repository) }
    # Check connectivity/authentication before making a commit. The GitHub repository must exist.
    Invoke-RepoGit -GitArguments @('ls-remote', 'origin')
    Invoke-RepoGit -GitArguments (@('add', '--') + $sourceFiles)
    Invoke-RepoGit -GitArguments @('diff', '--cached', '--check')
    & git @gitCommon diff --cached --quiet
    $differenceStatus = $LASTEXITCODE
    if ($differenceStatus -eq 1) {
        $authorName = & git @gitCommon config user.name
        if ([string]::IsNullOrWhiteSpace($authorName)) { $authorName = 'Pablo Rivas' }
        $authorEmail = & git @gitCommon config user.email
        if ([string]::IsNullOrWhiteSpace($authorEmail)) { $authorEmail = 'elkhantar@users.noreply.github.com' }
        Invoke-RepoGit -GitArguments @('-c', "user.name=$authorName", '-c', "user.email=$authorEmail", 'commit', '-m', 'Add QuickCrop with adversarial fixes, documentation, and MIT license')
    } elseif ($differenceStatus -ne 0) { throw 'Unable to inspect staged changes.' }
    Invoke-RepoGit -GitArguments @('push', '-u', 'origin', 'HEAD:main')
} finally { Pop-Location }
