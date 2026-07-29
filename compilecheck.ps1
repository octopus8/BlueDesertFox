$ErrorActionPreference = 'Stop'

$version = (Select-String -Path 'ProjectSettings\ProjectVersion.txt' -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value.Trim()
$unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Data"
$dotnet = @("$unity\DotNetSdk\dotnet.exe", "$unity\NetCoreRuntime\dotnet.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
$csc = @("$unity\DotNetSdkRoslyn\csc.dll") + (Get-ChildItem "$unity\DotNetSdk\sdk" -Filter 'csc.dll' -Recurse -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }) |
    Where-Object { Test-Path $_ } | Select-Object -First 1
$root = (Get-Location).Path

# Directories owned by their own assembly definition are compiled separately, as are Editor-only
# folders (Assembly-CSharp-Editor) and the firstpass plugin folders.
$asmdefDirs = Get-ChildItem 'Assets' -Filter '*.asmdef' -Recurse | ForEach-Object { $_.DirectoryName }
$firstpass = @('Assets\Plugins', 'Assets\Standard Assets', 'Assets\Pro Standard Assets') |
    ForEach-Object { Join-Path $root $_ }

$sources = Get-ChildItem 'Assets' -Filter '*.cs' -Recurse | Where-Object {
    $dir = $_.DirectoryName
    if ($asmdefDirs | Where-Object { $dir -eq $_ -or $dir.StartsWith($_ + '\') }) { return $false }
    if ($firstpass | Where-Object { $dir -eq $_ -or $dir.StartsWith($_ + '\') }) { return $false }
    if ($_.FullName -match '(\\|^)Editor(\\|$)') { return $false }
    return $true
} | ForEach-Object { $_.FullName }

$refs = @()
$refs += Get-ChildItem 'Library\ScriptAssemblies' -Filter '*.dll' |
    Where-Object { $_.Name -ne 'Assembly-CSharp.dll' } |
    ForEach-Object { $_.FullName }
$refs += Get-ChildItem (Join-Path $unity 'Managed\UnityEngine') -Filter '*.dll' -ErrorAction SilentlyContinue |
    ForEach-Object { $_.FullName }
$refs += (Join-Path $unity 'Managed\UnityEngine.dll')
$refs += (Join-Path $unity 'Managed\UnityEditor.dll')
# Unity 6 compiles user assemblies against netstandard 2.1 plus its compat shims, not the 4.8 profile.
$refs += (Join-Path $unity 'NetStandard\ref\2.1.0\netstandard.dll')
foreach ($shim in 'netstandard', 'netfx') {
    $refs += Get-ChildItem (Join-Path $unity "NetStandard\compat\2.1.0\shims\$shim") -Filter '*.dll' -ErrorAction SilentlyContinue |
        ForEach-Object { $_.FullName }
}
$refs += Get-ChildItem (Join-Path $unity 'NetStandard\Extensions\2.0.0') -Filter '*.dll' -ErrorAction SilentlyContinue |
    ForEach-Object { $_.FullName }
# Precompiled plugins (Odin attributes, DOTween, InputSimulator). The editor-capable Sirenix variants
# are the ones in play because UNITY_EDITOR is defined below, so skip the stripped duplicates.
$refs += Get-ChildItem 'Assets' -Filter '*.dll' -Recurse |
    Where-Object { $_.FullName -notmatch '\\(Editor|NoEditor|NoEmitAndNoEditor)\\' } |
    ForEach-Object { $_.FullName }

$refs = $refs | Where-Object { Test-Path $_ } | Sort-Object -Unique

$defines = @(
    'UNITY_EDITOR', 'UNITY_EDITOR_WIN', 'UNITY_EDITOR_64',
    'UNITY_STANDALONE_WIN', 'UNITY_STANDALONE', 'UNITY_64',
    'ENABLE_INPUT_SYSTEM', 'ENABLE_LEGACY_INPUT_MANAGER', 'ENABLE_VR', 'ENABLE_PROFILER',
    'CSHARP_7_OR_LATER', 'CSHARP_7_3_OR_NEWER', 'UNITY_INCLUDE_TESTS',
    'DEBUG', 'TRACE', 'UNITY_ASSERTIONS'
)
foreach ($major in 2017..2023) { foreach ($minor in 1..4) { $defines += "UNITY_${major}_${minor}_OR_NEWER" }; $defines += "UNITY_${major}_OR_NEWER" }
foreach ($minor in 0..5) { $defines += "UNITY_6000_${minor}_OR_NEWER" }
$defines += @('UNITY_6000_OR_NEWER', 'UNITY_6_OR_NEWER')

$out = Join-Path $env:TEMP 'bdf-compilecheck'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$rsp = Join-Path $out 'compilecheck.rsp'
$lines = @(
    '-target:library'
    '-nostdlib+'
    '-noconfig'
    '-langversion:9.0'
    '-nullable:disable'
    '-unsafe+'
    "-out:`"$out\Assembly-CSharp.dll`""
    "-define:$($defines -join ';')"
)
$lines += $refs | ForEach-Object { "-r:`"$_`"" }
$lines += $sources | ForEach-Object { "`"$_`"" }
Set-Content -Path $rsp -Value $lines -Encoding UTF8

$log = Join-Path $out 'compileout.txt'
Write-Output "sources: $($sources.Count)  refs: $($refs.Count)"
& $dotnet $csc "@$rsp" 2>&1 | Out-File -FilePath $log -Encoding UTF8
Write-Output "exit: $LASTEXITCODE  log: $log"
Select-String -Path $log -Pattern ': error ' | ForEach-Object { $_.Line }
