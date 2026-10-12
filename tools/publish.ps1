param([string]$Output='artifacts/windows/PDFToolbox')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 dotnet publish src/PDFToolbox.Windows -c Release --no-restore -o $Output
 if($LASTEXITCODE) {throw 'Publish failed.'}
 foreach($file in @('README.md','LICENSE','THIRD-PARTY-NOTICES.txt','Windows-SDK-License.rtf')) {
  Copy-Item -LiteralPath $file -Destination $Output
 }
} finally {Pop-Location}
