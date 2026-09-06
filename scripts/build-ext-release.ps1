param(
  [ValidateSet("win-x64")]
  [string] $Runtime = "win-x64",
  [string] $Configuration = "Release",
  [string] $InnoSetupPath = "",
  [switch] $SkipInstaller
)

$ErrorActionPreference = "Stop"

function Resolve-Dotnet {
  $repoRootLocal = Split-Path -Parent $PSScriptRoot
  $bundledDotnet = Join-Path (Split-Path -Parent $repoRootLocal) ".dotnet-sdk-10\dotnet.exe"
  $programFilesDotnet = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
  if ($env:LLAMA_CPP_WINDOWS_MANAGER_DOTNET) { return $env:LLAMA_CPP_WINDOWS_MANAGER_DOTNET }
  if ($env:LLAMA_CPP_CONSOLE_DOTNET) { return $env:LLAMA_CPP_CONSOLE_DOTNET }
  if ($env:LOCAL_LLM_CONSOLE_DOTNET) { return $env:LOCAL_LLM_CONSOLE_DOTNET }
  if (Test-Path -LiteralPath $bundledDotnet) { return $bundledDotnet }
  if (Test-Path -LiteralPath $programFilesDotnet) { return $programFilesDotnet }
  $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
  if ($command) { return $command.Source }
  return ""
}

function Remove-DistPath {
  param([string] $Path, [string] $Label = "")
  if (Test-Path -LiteralPath $Path) {
    Remove-Item -LiteralPath $Path -Recurse -Force
  }
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Scripts = Join-Path $RepoRoot "scripts"
$DistRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot "dist"))
$PublishDir = [System.IO.Path]::GetFullPath((Join-Path $DistRoot "LlamaCppWindowsManager-$Runtime"))
$ZipPath = [System.IO.Path]::GetFullPath((Join-Path $DistRoot "LlamaCppWindowsManager-$Runtime.zip"))
$Dotnet = Resolve-Dotnet
if (-not $Dotnet) { throw ".NET SDK was not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0." }
if (-not (Test-Path -LiteralPath $Dotnet)) { throw "Configured dotnet path was not found: $Dotnet" }

Write-Host ""
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "  EXT RELEASE BUILD (App + Service + Updater + llwmctl)" -ForegroundColor Cyan
Write-Host "  dotnet : $Dotnet" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan

# --- 1) Чистый PublishDir с нуля (никакого мусора от прошлых сборок) ---
Remove-DistPath -Path $PublishDir -Label "stale publish dir"
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

# --- 2) Единый набор флагов публикации (ext: БЕЗ AgentBootstrapBundlePath!) ---
$commonPublish = @(
  "publish", "", "-c", $Configuration, "-r", $Runtime,
  "--self-contained", "true",
  "-p:PublishSingleFile=true",
  "-p:IncludeNativeLibrariesForSelfExtract=true",
  "-p:EnableCompressionInSingleFile=true",
  "-o", $PublishDir
)

$projects = @(
  @{ Name = "App (LlamaCppWindowsManager.exe)";      Csproj = "src\LocalLlmConsole.App\LocalLlmConsole.App.csproj" },
  @{ Name = "Service (LocalLlmConsole.Service.exe)"; Csproj = "src\LocalLlmConsole.Service\LocalLlmConsole.Service.csproj" },
  @{ Name = "Updater (LocalLlmConsole.Updater.exe)"; Csproj = "src\LocalLlmConsole.Updater\LocalLlmConsole.Updater.csproj" },
  @{ Name = "llwmctl (ControlCli)";                  Csproj = "src\LocalLlmConsole.ControlCli\LocalLlmConsole.ControlCli.csproj" }
)

foreach ($project in $projects) {
  Write-Host ""
  Write-Host "==> $($project.Name)" -ForegroundColor Cyan
  $args = @($commonPublish)
  $args[1] = Join-Path $RepoRoot $project.Csproj
  & $Dotnet @args
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($project.Name)." }
}

# --- 3) Доки/лицензии в PublishDir (SourceDir для Setup) ---
Write-Host ""
Write-Host "==> доки/лицензии в PublishDir (для Setup)" -ForegroundColor Cyan
$bundleFiles = @(
  "AGENTS.md",
  "agent.md",
  "LICENSE",
  "THIRD-PARTY-NOTICES.md"
)
foreach ($file in $bundleFiles) {
  Copy-Item -LiteralPath (Join-Path $RepoRoot $file) -Destination (Join-Path $PublishDir $file) -Force
}
foreach ($doc in @("CONTROL_API.md", "LLAMAMANAGER-SERVICE.md")) {
  $destDir = Join-Path $PublishDir "docs"
  New-Item -ItemType Directory -Path $destDir -Force | Out-Null
  Copy-Item -LiteralPath (Join-Path $RepoRoot "docs\$doc") -Destination (Join-Path $destDir $doc) -Force
}
$licDir = Join-Path $PublishDir "licenses"
New-Item -ItemType Directory -Path $licDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $RepoRoot "licenses\Apache-2.0.txt") -Destination (Join-Path $licDir "Apache-2.0.txt") -Force
$dotnetRoot = Split-Path -Parent ([System.IO.Path]::GetFullPath($Dotnet))
$dotnetLicDir = Join-Path $licDir "dotnet"
New-Item -ItemType Directory -Path $dotnetLicDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $dotnetRoot "LICENSE.txt") -Destination (Join-Path $dotnetLicDir "LICENSE.txt") -Force
Copy-Item -LiteralPath (Join-Path $dotnetRoot "ThirdPartyNotices.txt") -Destination (Join-Path $dotnetLicDir "ThirdPartyNotices.txt") -Force

# --- 4) SBOM (нужен Setup, лежит в PublishDir) ---
Write-Host ""
Write-Host "==> sbom.spdx.json" -ForegroundColor Cyan
[xml] $projectXml = Get-Content -LiteralPath (Join-Path $RepoRoot "src\LocalLlmConsole.App\LocalLlmConsole.App.csproj")
$appVersion = @($projectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if (-not $appVersion) { throw "Cannot read Version from App.csproj." }
$sbomPath = Join-Path $PublishDir "sbom.spdx.json"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Scripts "new-sbom.ps1") -OutputPath $sbomPath -Version $appVersion
if ($LASTEXITCODE -ne 0) { throw "SPDX SBOM generation failed." }

# --- 5) Зачистка мусора: НИКАКОГО pdb/runtimeconfig/deps в dist ---
Write-Host ""
Write-Host "==> зачистка мусора (pdb/runtimeconfig/deps)" -ForegroundColor Cyan
Get-ChildItem -Path $PublishDir -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object { $_.Extension -in @(".pdb", ".deps.json") -or $_.Name -like "*.runtimeconfig.json" } |
  Remove-Item -Force

# --- 6) sha256 для ВСЕХ 4 exe ---
Write-Host ""
Write-Host "==> sha256 для 4 exe" -ForegroundColor Cyan
$exeNames = @("LlamaCppWindowsManager.exe", "LocalLlmConsole.Service.exe", "LocalLlmConsole.Updater.exe", "llwmctl.exe")
foreach ($exeName in $exeNames) {
  $exe = Join-Path $PublishDir $exeName
  if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Missing expected executable: $exe" }
  $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
  Set-Content -LiteralPath "$exe.sha256" -Value "$hash  $exeName" -Encoding ascii
  $size = (Get-Item -LiteralPath $exe).Length
  Write-Host "  $exeName  $([math]::Round($size / 1MB, 1)) MB  $hash" -ForegroundColor Gray
}

# --- 7) ZIP = РОВНО 4 exe (эталон 2.3.2.8), НЕ полный набор из dist ---
Write-Host ""
Write-Host "==> zip из ровно 4 exe (без доков/pdb/мусора)" -ForegroundColor Cyan
Remove-DistPath -Path $ZipPath -Label "stale zip"
$staging = Join-Path $DistRoot ".ext-zip-$Runtime"
Remove-DistPath -Path $staging -Label "stale zip staging"
New-Item -ItemType Directory -Path $staging -Force | Out-Null
foreach ($exeName in $exeNames) {
  Copy-Item -LiteralPath (Join-Path $PublishDir $exeName) -Destination (Join-Path $staging $exeName) -Force
}
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $ZipPath -Force
Remove-DistPath -Path $staging -Label "zip staging"
$zipHash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$ZipPath.sha256" -Value "$zipHash  $(Split-Path -Leaf $ZipPath)" -Encoding ascii

# --- 8) Контроль zip: ровно 4 записи, без pdb/json/md ---
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
  $entries = @($zip.Entries | ForEach-Object { $_.FullName -replace "\\", "/" })
} finally {
  $zip.Dispose()
}
if ($entries.Count -ne 4) { throw "Release zip must contain exactly 4 executables, found $($entries.Count): $($entries -join ', ')" }
foreach ($exeName in $exeNames) {
  if ($entries -notcontains $exeName) { throw "Release zip is missing $exeName" }
}
Write-Host "OK: zip содержит ровно 4 exe: $($entries -join ', ')" -ForegroundColor Green

# --- 9) Setup (Inno) ---
if (-not $SkipInstaller) {
  Write-Host ""
  Write-Host "==> build-installer.ps1 (Setup, -SkipPublish)" -ForegroundColor Cyan
  $installerArgs = @("-Runtime", $Runtime, "-Configuration", $Configuration, "-SkipPublish")
  if ($InnoSetupPath) { $installerArgs += @("-InnoSetupPath", $InnoSetupPath) }
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Scripts "build-installer.ps1") @installerArgs
  if ($LASTEXITCODE -ne 0) { throw "build-installer.ps1 failed." }
}

Write-Host ""
Write-Host "==============================================" -ForegroundColor Green
Write-Host "  EXT RELEASE BUILD OK" -ForegroundColor Green
Write-Host "  PublishDir : $PublishDir" -ForegroundColor Green
Write-Host "  Zip        : $ZipPath (ровно 4 exe)" -ForegroundColor Green
if (-not $SkipInstaller) {
  Write-Host "  Setup      : dist\installer\LlamaCppWindowsManager-Setup-$appVersion-win-x64.exe" -ForegroundColor Green
}
Write-Host "  Ассеты для GitHub: 5 файлов + 5 .sha256 (App, Service, Updater, zip, Setup)" -ForegroundColor Green
Write-Host "==============================================" -ForegroundColor Green
