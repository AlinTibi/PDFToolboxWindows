param([string]$PackagePath='artifacts/windows/PDFToolbox', [string]$ExpectedVersion='1.0.0')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$package=if([IO.Path]::IsPathRooted($PackagePath)) {$PackagePath} else {Join-Path $root $PackagePath}
foreach($file in @('PDFToolbox.exe','PDFToolbox.dll','PdfEngine.dll','PDFToolbox.deps.json','PDFToolbox.runtimeconfig.json','coreclr.dll','hostfxr.dll','hostpolicy.dll','LICENSE','THIRD-PARTY-NOTICES.txt','Windows-SDK-License.rtf','README.txt')) {
 if(!(Test-Path -LiteralPath (Join-Path $package $file))) {throw "Missing portable component: $file"}
}
$info=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $package 'PDFToolbox.exe'))
if($info.ProductName -ne 'PDF Toolbox' -or $info.CompanyName -ne 'ALMARFELD' -or $info.ProductVersion -ne $ExpectedVersion -or $info.FileVersion -ne "$ExpectedVersion.0" -or $info.FileDescription -ne 'PDF Toolbox — offline PDF utilities' -or $info.LegalCopyright -ne '© 2026 ALMARFELD') {throw 'Executable release metadata mismatch.'}
if((Get-AuthenticodeSignature (Join-Path $package 'PDFToolbox.exe')).Status -ne 'NotSigned') {throw 'Unexpected application Authenticode status.'}
$forbidden=Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object {$_.Extension -in @('.pdb','.cs','.csproj','.sln','.log','.tmp','.pdf','.png','.jpg') -or $_.Name -like '*Tests*'}
if($forbidden) {throw 'Development/test payload found in portable output.'}
foreach($name in @('PDFToolbox.exe','PDFToolbox.dll','PdfEngine.dll')) {
 $bytes=[IO.File]::ReadAllBytes((Join-Path $package $name))
 foreach($encoding in @([Text.Encoding]::UTF8,[Text.Encoding]::Unicode)) {
  $text=$encoding.GetString($bytes)
  if($text -match '[A-Z]:\\Users\\|[A-Z]:\\[^\\]+\\PDFToolboxWindows\\|[a-z0-9._%+-]+@gmail\.com|synthetic-test-password') {throw 'Local/test data found in product binary.'}
 }
}
Write-Output "Portable validation PASS: $ExpectedVersion, self-contained, clean payload, unsigned executable."
