<#
.SYNOPSIS
  Builds OpenRouterBar from source and installs it for the current user (no admin rights needed).
.DESCRIPTION
  Installs to %LOCALAPPDATA%\Programs\OpenRouterBar, starts it at sign-in through the per-user Run
  key, and starts it now. An existing key.dat and settings.ini in the install folder are kept.
.PARAMETER Uninstall
  Stops the widget, removes the Run entry, and deletes the install folder, including key.dat.
.PARAMETER NoAutostart
  Install without starting at sign-in.
#>
[CmdletBinding()]
param([switch]$Uninstall, [switch]$NoAutostart)
$ErrorActionPreference = "Stop"
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  Write-Warning "Running as administrator. Installing for $env:USERNAME; the widget itself is started with normal rights. If you elevated with a different account, run this again from your own normal PowerShell window."
}

$installDir = Join-Path $env:LOCALAPPDATA "Programs\OpenRouterBar"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$exe = Join-Path $installDir "OpenRouterBar.exe"

# Stops only this user's installed copy, and waits until its exe is no longer locked.
function Stop-Widget {
  Get-Process -Name OpenRouterBar -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $exe } |
    ForEach-Object { Stop-Process -Id $_.Id -Force; Wait-Process -Id $_.Id -Timeout 5 -ErrorAction SilentlyContinue }
}

if ($Uninstall) {
  Stop-Widget
  Remove-ItemProperty -Path $runKey -Name OpenRouterBar -ErrorAction SilentlyContinue
  if (Test-Path -LiteralPath $installDir) { Remove-Item -LiteralPath $installDir -Recurse -Force }
  Write-Host "OpenRouterBar uninstalled."
  return
}

# Run build.cmd by its bare name from the clone folder, so cmd.exe never parses the clone path.
Push-Location -LiteralPath $PSScriptRoot
try { & cmd.exe /d /c build.cmd } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Stop-Widget
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
# Windows can hold the old exe open for a moment after the process exits.
for ($i = 1; ; $i++) {
  try { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "bin\OpenRouterBar.exe") -Destination $installDir -Force; break }
  catch [System.IO.IOException] { if ($i -ge 20) { throw }; Start-Sleep -Milliseconds 250 }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "set-key.ps1") -Destination $installDir -Force

if ($NoAutostart) {
  Remove-ItemProperty -Path $runKey -Name OpenRouterBar -ErrorAction SilentlyContinue
} else {
  Set-ItemProperty -Path $runKey -Name OpenRouterBar -Value ('"' + $exe + '"')
}

# Started through Explorer so it runs with the shell's normal rights even from an elevated window.
Start-Process -FilePath (Join-Path $env:WINDIR "explorer.exe") -ArgumentList ('"' + $exe + '"')
Write-Host "Installed to $installDir"
if (-not (Test-Path -LiteralPath (Join-Path $installDir "key.dat"))) {
  Write-Host "Next: store your key with  powershell -NoProfile -ExecutionPolicy Bypass -File `"$installDir\set-key.ps1`""
}
