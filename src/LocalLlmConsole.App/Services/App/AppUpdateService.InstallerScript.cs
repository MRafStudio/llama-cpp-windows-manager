namespace LocalLlmConsole.Services;

public sealed partial class AppUpdateService
{
    private static string UpdaterScript() => """
param(
  [int] $ParentPid,
  [string] $SourceExe,
  [string] $TargetExe,
  [string] $ObsoleteExe,
  [string] $SourceCli,
  [string] $TargetCli,
  [string] $NoticeSource,
  [string] $NoticeTarget,
  [string] $WorkingDirectory,
  [string] $SourceService,
  [string] $TargetService,
  [string] $ServiceName
)
$ErrorActionPreference = "Stop"

# Обновление службы требует прав администратора. Если служба установлена,
# а мы не админ — запрашиваем повышение (UAC). Отказ — выходим без изменений.
if ($ServiceName -and (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
  $principal = New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
  $isAdmin = $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
  if (-not $isAdmin) {
    $self = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    $args = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID").CommandLine
    try {
      Start-Process -FilePath $self -ArgumentList $args -Verb RunAs
    } catch {
      exit 0
    }
    exit 0
  }
}

function Remove-UpdateArtifact {
  param([string] $Path)
  if (-not $Path) { return }
  for ($attempt = 0; $attempt -lt 50; $attempt++) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
      [System.IO.File]::SetAttributes($Path, [System.IO.FileAttributes]::Normal)
      [System.IO.File]::Delete($Path)
      if (-not (Test-Path -LiteralPath $Path)) { return }
    } catch {
      if ($attempt -eq 49) {
        Write-Warning ("Could not remove update artifact '{0}': {1}" -f $Path, $_.Exception.Message)
      }
    }
    if ($attempt -lt 49) { Start-Sleep -Milliseconds 100 }
  }
}

function Get-UpdateFileSha256 {
  param([string] $Path)
  $stream = [System.IO.File]::OpenRead($Path)
  try {
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
      return [System.BitConverter]::ToString($sha256.ComputeHash($stream)).Replace("-", "")
    } finally {
      $sha256.Dispose()
    }
  } finally {
    $stream.Dispose()
  }
}

function New-VerifiedStage {
  param([string] $Source, [string] $Target)
  if (-not $Source -or -not $Target -or -not (Test-Path -LiteralPath $Source -PathType Leaf)) { return $null }
  $targetDirectory = Split-Path -Parent $Target
  New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
  $temporary = Join-Path $targetDirectory ("." + (Split-Path -Leaf $Target) + "." + [Guid]::NewGuid().ToString("N") + ".new")
  try {
    Copy-Item -LiteralPath $Source -Destination $temporary
    $sourceHash = Get-UpdateFileSha256 -Path $Source
    $stagedHash = Get-UpdateFileSha256 -Path $temporary
    if ($sourceHash -ne $stagedHash) {
      throw "Staged update verification failed for $Target"
    }
    return [pscustomobject]@{ Target = $Target; Temporary = $temporary; Backup = ""; HadOriginal = (Test-Path -LiteralPath $Target) }
  } catch {
    Remove-UpdateArtifact -Path $temporary
    throw
  }
}

function Commit-VerifiedStage {
  param($Stage)
  if ($null -eq $Stage) { return }
  if ($Stage.HadOriginal) {
    $Stage.Backup = Join-Path (Split-Path -Parent $Stage.Target) ("." + (Split-Path -Leaf $Stage.Target) + "." + [Guid]::NewGuid().ToString("N") + ".bak")
    [System.IO.File]::Replace($Stage.Temporary, $Stage.Target, $Stage.Backup, $true)
  } else {
    [System.IO.File]::Move($Stage.Temporary, $Stage.Target)
  }
}

function Restore-CommittedStage {
  param($Stage)
  if ($null -eq $Stage) { return }
  if ($Stage.HadOriginal -and $Stage.Backup -and (Test-Path -LiteralPath $Stage.Backup)) {
    if (Test-Path -LiteralPath $Stage.Target) {
      $discard = $Stage.Target + "." + [Guid]::NewGuid().ToString("N") + ".rollback"
      [System.IO.File]::Replace($Stage.Backup, $Stage.Target, $discard, $true)
      Remove-UpdateArtifact -Path $discard
    } else {
      [System.IO.File]::Move($Stage.Backup, $Stage.Target)
    }
  } elseif (-not $Stage.HadOriginal -and (Test-Path -LiteralPath $Stage.Target)) {
    Remove-Item -LiteralPath $Stage.Target -Force
  }
}

# Ждём закрытия приложения максимум 10 секунд (поллинг 250 мс).
# Не закрылось по-доброму — принудительно убиваем: никаких долгих ожиданий.
$deadline = (Get-Date).AddSeconds(10)
while ((Get-Date) -lt $deadline -and (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue)) {
  Start-Sleep -Milliseconds 250
}
if (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue) {
  Stop-Process -Id $ParentPid -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500
}
Start-Sleep -Milliseconds 500
$stages = @()
$committed = @()
try {
  $appStage = New-VerifiedStage -Source $SourceExe -Target $TargetExe
  if ($null -eq $appStage) { throw "The staged application executable is missing." }
  $stages += $appStage
  $cliStage = New-VerifiedStage -Source $SourceCli -Target $TargetCli
  if ($null -ne $cliStage) { $stages += $cliStage }
  foreach ($stage in $stages) {
    Commit-VerifiedStage -Stage $stage
    $committed += $stage
  }
} catch {
  for ($index = $committed.Count - 1; $index -ge 0; $index--) {
    try { Restore-CommittedStage -Stage $committed[$index] } catch {}
  }
  throw
} finally {
  foreach ($stage in $stages) {
    Remove-UpdateArtifact -Path $stage.Temporary
    Remove-UpdateArtifact -Path $stage.Backup
  }
}
if ($ObsoleteExe -and
    -not [string]::Equals($ObsoleteExe, $TargetExe, [System.StringComparison]::OrdinalIgnoreCase) -and
    (Test-Path -LiteralPath $ObsoleteExe)) {
  Remove-Item -LiteralPath $ObsoleteExe -Force
}
if (Test-Path -LiteralPath $NoticeSource) {
  New-Item -ItemType Directory -Path (Split-Path -Parent $NoticeTarget) -Force | Out-Null
  Copy-Item -LiteralPath $NoticeSource -Destination $NoticeTarget -Force
}
# Обновление службы: останавливаем, заменяем exe, запускаем.
if ($SourceService -and $ServiceName -and $TargetService) {
  try {
    Stop-Service -Name $ServiceName -Force -ErrorAction Stop
    Start-Sleep -Milliseconds 500
  } catch {}
  $serviceStage = New-VerifiedStage -Source $SourceService -Target $TargetService
  if ($null -ne $serviceStage) {
    Commit-VerifiedStage -Stage $serviceStage
    Remove-UpdateArtifact -Path $serviceStage.Temporary
    Remove-UpdateArtifact -Path $serviceStage.Backup
  }
  try {
    Start-Service -Name $ServiceName -ErrorAction Stop
  } catch {}
}
Start-Process -FilePath $TargetExe -WorkingDirectory $WorkingDirectory | Out-Null
""";
}
