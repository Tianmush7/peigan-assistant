[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'src/YingBan.App/YingBan.App.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Unexpected application version.' }
$webViewVersion = [string]($projectXml.Project.ItemGroup.PackageReference | Where-Object Include -eq 'Microsoft.Web.WebView2').Version
if ($webViewVersion -ne '1.0.4258.31') { throw 'Refresh third-party notices for the updated WebView2 version before packaging.' }
$dist = Join-Path $root 'dist'
$buildRoot = Join-Path $dist ('build-' + [Guid]::NewGuid().ToString('N'))
$application = Join-Path $buildRoot 'PeiganAssistant-Windows-x64'
$archive = Join-Path $dist "PeiganAssistant-v$version-win-x64.zip"
if (Test-Path -LiteralPath $archive) { throw "Archive already exists: $archive. Move it aside before packaging again." }
New-Item -ItemType Directory -Path $application -Force | Out-Null
# Pin the runtime so that the included redistribution notices match this build.
& dotnet publish $project -c Release -r win-x64 --self-contained true -o $application '-p:RuntimeFrameworkVersion=10.0.0' '-p:TargetLatestRuntimePatch=false' '-p:ContinuousIntegrationBuild=true' "-p:PathMap=$root=/_/peigan-assistant"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
foreach ($file in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $application
}
Copy-Item -LiteralPath (Join-Path $root 'licenses'),(Join-Path $root 'docs') -Destination $application -Recurse
$scriptPath = Join-Path $application 'Assets/vendor/btr/bilibili-thread-ripper.user.js'
if ((Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash -ne 'A21F17028406DF43BC71A3AF17E07A2E490582480A0DB275F693CE624501B97A') { throw 'Unexpected BTR script; review and update the pinned version before packaging.' }
if (!(Test-Path -LiteralPath (Join-Path $application 'PeiganAssistant.exe'))) { throw 'Application executable missing.' }
[IO.Compression.ZipFile]::CreateFromDirectory($application, $archive, [IO.Compression.CompressionLevel]::Optimal, $true)
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding utf8
Write-Output "Portable archive: $archive"
Write-Output "Application folder: $application"
