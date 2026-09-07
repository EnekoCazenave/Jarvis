param([switch]$Test, [switch]$WindowsTest)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x Windows est requis.' }
$output = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -Recurse | ForEach-Object FullName)
& $compiler /nologo /warnaserror+ /target:winexe /main:Jarvis.Program /out:"$output\Jarvis.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation de Jarvis échouée.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'witnesses') -Destination $output -Recurse -Force
if ($Test -or $WindowsTest) {
    $tests = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object FullName)
    & $compiler /nologo /warnaserror+ /target:exe /main:Jarvis.Tests.TestRunner /out:"$output\Jarvis.Tests.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll $sources $tests
    if ($LASTEXITCODE -ne 0) { throw 'Compilation des tests échouée.' }
    if ($WindowsTest) { & "$output\Jarvis.Tests.exe" --windows } else { & "$output\Jarvis.Tests.exe" }
    if ($LASTEXITCODE -ne 0) { throw 'Tests échoués.' }
}
