<#
.SYNOPSIS
  Stores an OpenRouter API key for OpenRouterBar, encrypted with Windows DPAPI for the current user.
.DESCRIPTION
  Writes key.dat to %LOCALAPPDATA%\Programs\OpenRouterBar, where the widget reads it. DPAPI ties it to
  your Windows account (administrators of the PC or domain can also recover it; see SECURITY.md).
  The key is read from standard input when input is redirected, otherwise from a hidden prompt, so
  it never appears on a command line or in shell history.
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File set-key.ps1
.EXAMPLE
  From WSL: powershell.exe -NoProfile -ExecutionPolicy Bypass -File set-key.ps1 < ~/.config/openrouter/key
#>
$ErrorActionPreference = "Stop"
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  Write-Warning "Running as administrator. The key is saved for $env:USERNAME; if you elevated with a different account, run this from your own normal PowerShell window instead."
}
Add-Type -AssemblyName System.Security

if ([Console]::IsInputRedirected) {
  $key = [Console]::In.ReadToEnd().Trim()
} else {
  $secure = Read-Host -Prompt "OpenRouter API key" -AsSecureString
  $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
  try { $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr).Trim() }
  finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
$key = $key.Trim([char]0xFEFF).Trim()
if (-not $key) { throw "No key given." }
if ($key -cnotmatch '^sk-or-[A-Za-z0-9_-]+$') { throw "That is not an OpenRouter API key (expected sk-or-... on a single line). Nothing was saved." }

$bytes = [Text.Encoding]::UTF8.GetBytes($key)
$key = $null
$enc = [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect($bytes, $null, "CurrentUser"))
[Array]::Clear($bytes, 0, $bytes.Length)
$installDir = Join-Path $env:LOCALAPPDATA "Programs\OpenRouterBar"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Set-Content -LiteralPath (Join-Path $installDir "key.dat") -Value $enc -Encoding ASCII
Write-Host "Saved key.dat (encrypted for $env:USERNAME). OpenRouterBar uses it at its next check."
