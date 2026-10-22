param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [string]$Javac = 'javac'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$classes = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $classes | Out-Null
& $Javac --release 17 -d $classes (Join-Path $PSScriptRoot 'HarnessSmoke.java')
if ($LASTEXITCODE -ne 0) { throw 'Smoke harness compilation failed.' }
$jar = Join-Path $GameRoot 'projectzomboid.jar'
$stdlib = Join-Path $GameRoot 'stdlib.lua'
Get-FileHash -Algorithm SHA256 -LiteralPath $jar, $stdlib | Format-List
foreach ($fixture in @('containment.lua', 'unrecoverable.lua')) {
    & (Join-Path $GameRoot 'jre64/bin/java.exe') -cp "$classes;$jar" HarnessSmoke `
        (Join-Path $repo 'Resources/Testing/Harness.lua') (Join-Path $PSScriptRoot $fixture) $stdlib
    if ($LASTEXITCODE -ne 0) { throw "Kahlua fixture failed: $fixture" }
}
