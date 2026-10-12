param([string]$Output='artifacts/windows/PDFToolbox')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 dotnet publish src/PDFToolbox.Windows -c Release --no-restore -p:DebugType=None -p:DebugSymbols=false -o $Output
 if($LASTEXITCODE) {throw 'Publish failed.'}
 foreach($file in @('LICENSE','THIRD-PARTY-NOTICES.txt','Windows-SDK-License.rtf')) {
  Copy-Item -LiteralPath $file -Destination $Output
 }
 $instructions=@'
PDF Toolbox v1.0.0 — ALMARFELD

Extract the whole ZIP and keep all adjacent runtime files/folders together.
Run PDFToolbox.exe on Windows 10 version 2004 or later / Windows 11 x64.
No separate .NET runtime or WebView2 installation is required.
Windows 11 was tested; Windows 10 has not been independently tested.

Open files, choose a tool, review options and export a new copy.
Processing stays on your device. No uploads, accounts, ads or automatic downloads.
Keep originals and inspect exported copies; advanced PDF preservation is limited.
Lossless compression may not shrink an already optimized PDF.

The executable is not Authenticode signed. Windows may show a reputation warning.
Use official release links and optionally verify the published SHA-256.
Do not disable Windows security.

Documentation: https://github.com/AlinTibi/PDFToolboxWindows
Support: https://almarfeld.com/support/
'@
 $destination=if([IO.Path]::IsPathRooted($Output)) {$Output} else {Join-Path $root $Output}
 [IO.File]::WriteAllText((Join-Path $destination 'README.txt'),$instructions,[Text.UTF8Encoding]::new($false))
} finally {Pop-Location}
