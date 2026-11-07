param([string]$OutputPath = '')
$ErrorActionPreference = 'Stop'
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot 'publish\win-x64' }
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
Push-Location $PSScriptRoot
try {
    # No SDK or separate .NET 8/9 runtime is deployed to the server.
    dotnet publish 'src/BKDayzModDownloader/BKDayzModDownloader.csproj' -c Release -r win-x64 --self-contained false -o $OutputPath -p:DebugType=None --nologo
    if ($LASTEXITCODE -ne 0) { throw 'WPF publish failed.' }
    dotnet publish 'src/BKDownloader.Engine/BKDownloader.Engine.csproj' -c Release -r win-x64 --self-contained false -o (Join-Path $OutputPath 'Engine') -p:DebugType=None --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Engine publish failed.' }
    Copy-Item -LiteralPath 'LICENSE','THIRD-PARTY-NOTICES.md','README.md','Start.bat' -Destination $OutputPath -Force
    foreach ($package in Get-ChildItem -LiteralPath '.packages' -Directory) {
        foreach ($version in Get-ChildItem -LiteralPath $package.FullName -Directory) {
            $licenseTarget = Join-Path $OutputPath ('Licenses\{0}\{1}' -f $package.Name, $version.Name)
            New-Item -ItemType Directory -Path $licenseTarget -Force | Out-Null
            Get-ChildItem -LiteralPath $version.FullName -File | Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE)|\.nuspec$' } | Copy-Item -Destination $licenseTarget -Force
        }
    }
    $sourceFolder = Join-Path $OutputPath 'Source'
    New-Item -ItemType Directory -Path $sourceFolder -Force | Out-Null
    # Complete corresponding source, without build caches, SDKs, or downloaded mods.
    $sourceEntries = @('src','tests','vendor','Directory.Build.props','global.json','NuGet.Config','ModList.example.txt','publish.ps1','Start.bat','README.md','LICENSE','THIRD-PARTY-NOTICES.md')
    $staging = Join-Path $PSScriptRoot 'publish\source'
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    foreach ($entry in $sourceEntries) {
        $item = Get-Item -LiteralPath $entry
        if (-not $item.PSIsContainer) { Copy-Item -LiteralPath $entry -Destination $staging -Force; continue }
        $files = Get-ChildItem -LiteralPath $entry -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.git)[\\/]' }
        foreach ($file in $files) {
            $relative = [System.IO.Path]::GetRelativePath($PSScriptRoot, $file.FullName)
            $target = Join-Path $staging $relative
            New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
    }
    $sourceArchiveEntries = @($sourceEntries | ForEach-Object { Join-Path $staging $_ })
    Compress-Archive -LiteralPath $sourceArchiveEntries -DestinationPath (Join-Path $sourceFolder 'BKDayzModDownloader-source.zip') -Force
    # Downloads is user data: preserve it on disk, but never include it in a release.
    $packageEntries = @(Get-ChildItem -LiteralPath $OutputPath -File | Where-Object {
        $_.Name -match '^BK.*\.(exe|dll|json)$' -or $_.Name -in @('LICENSE','ModList.txt','README.md','Start.bat','THIRD-PARTY-NOTICES.md')
    }) + @(Get-Item -LiteralPath (Join-Path $OutputPath 'Engine'),(Join-Path $OutputPath 'Licenses'),(Join-Path $OutputPath 'Source'))
    $files = @($packageEntries | ForEach-Object { if ($_.PSIsContainer) { Get-ChildItem -LiteralPath $_.FullName -Recurse -File } else { $_ } })
    $size = ($files | Measure-Object -Property Length -Sum).Sum / 1MB
    $zip = Join-Path $PSScriptRoot 'publish\BKDayzModDownloader-win-x64.zip'
    Compress-Archive -LiteralPath $packageEntries.FullName -DestinationPath $zip -Force
    Write-Host ('Published to {0}; files: {1}; total: {2:N2} MiB' -f $OutputPath, $files.Count, $size)
} finally { Pop-Location }
