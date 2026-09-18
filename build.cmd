@echo off
rem Builds bin\OpenRouterBar.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
cd /d "%~dp0"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "CSC=%FW%\csc.exe"
if not exist "%CSC%" (echo csc.exe not found at %CSC% & exit /b 1)
if not exist bin mkdir bin
"%CSC%" /nologo /target:winexe /optimize+ /noconfig /langversion:5 /codepage:65001 /warnaserror+ ^
  /win32manifest:src\app.manifest /out:bin\OpenRouterBar.exe ^
  /r:"%FW%\System.dll" /r:"%FW%\System.Drawing.dll" /r:"%FW%\System.Windows.Forms.dll" /r:"%FW%\System.Security.dll" /r:"%FW%\System.Runtime.Serialization.dll" /r:"%FW%\System.Xml.dll" ^
  src\AssemblyInfo.cs src\Core.cs src\Widget.cs
exit /b %ERRORLEVEL%
