# Builds GidEditor for Windows as an installer (Inno Setup) and a portable .zip.
#
#   pwsh build/windows/package.ps1 -Rid win-x64 -Version 0.2.0 [-OutputDir publish]
#
# Supported RIDs: win-x64, win-arm64. Requires Inno Setup 6 (https://jrsoftware.org/isinfo.php).
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string]$Rid,
    [Parameter(Mandatory)][string]$Version,
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $OutputDir) { $OutputDir = Join-Path $root 'publish' }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = Resolve-Path $OutputDir
$arch = $Rid.Substring(4)

$work = Join-Path ([IO.Path]::GetTempPath()) ("gideditor-" + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $work 'GidEditor'

try {
    dotnet publish (Join-Path $root 'src\GidEditor\GidEditor.csproj') `
        -c Release -r $Rid --self-contained `
        -p:Version=$Version `
        -o $app
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    # Portable zip: unzip anywhere and run GidEditor.exe.
    $zip = Join-Path $OutputDir "GidEditor-$Version-$Rid.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path $app -DestinationPath $zip

    # Installer.
    $iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        $iscc = @(
            "${env:ProgramFiles(x86)}\Inno Setup *\ISCC.exe",
            "$env:ProgramFiles\Inno Setup *\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup *\ISCC.exe"
        ) | ForEach-Object { Get-ChildItem $_ -ErrorAction SilentlyContinue } |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found. Install it with: choco install innosetup" }

    & $iscc `
        "/DAppVersion=$Version" `
        "/DNumericVersion=$($Version.Split('-')[0])" `
        "/DSourceDir=$app" `
        "/DOutputDir=$OutputDir" `
        "/DArch=$arch" `
        (Join-Path $PSScriptRoot 'installer.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

    Write-Host "Created:"
    Get-ChildItem $OutputDir -Filter "GidEditor-$Version-*$arch*" | Format-Table Name, Length
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
