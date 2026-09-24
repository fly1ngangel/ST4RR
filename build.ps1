$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputDir = Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$referenceDir = Join-Path $projectRoot 'tools\microsoft.netframework.referenceassemblies.net48\build\.NETFramework\v4.8'
$compiler = Join-Path $projectRoot 'tools\microsoft.net.compilers.toolset\tasks\net472\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Run python tools/bootstrap.py first.' }
$libraries = @(
 (Join-Path $projectRoot 'tools\newtonsoft.json\lib\net45\Newtonsoft.Json.dll'),
 (Join-Path $projectRoot 'tools\litedb\lib\net45\LiteDB.dll'),
 (Join-Path $projectRoot 'tools\htmlagilitypack\lib\Net45\HtmlAgilityPack.dll')
)
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/deterministic+','/langversion:latest','/nostdlib+','/utf8output',('/out:"'+$outputDir+'\ST4RR.exe"'),('/win32manifest:"'+$projectRoot+'\src\app.manifest"'),('/win32icon:"'+$projectRoot+'\src\app.ico"'),('/resource:"'+$projectRoot+'\src\Theme.xaml",Theme.xaml'),('/resource:"'+$projectRoot+'\src\app.ico",app.ico'))
$arguments += Get-ChildItem -LiteralPath $referenceDir -Filter '*.dll' | Where-Object { $_.Name -notmatch 'EnterpriseServices\.(Wrapper|Thunk)' } | ForEach-Object { '/reference:"'+$_.FullName+'"' }
$arguments += $libraries | ForEach-Object { '/reference:"'+$_+'"' }
$arguments += Get-ChildItem (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { '"'+$_.FullName+'"' }
$responseFile = Join-Path $projectRoot 'tools\build.rsp'
[IO.File]::WriteAllLines($responseFile,$arguments,[Text.Encoding]::UTF8)
& $compiler ('@'+$responseFile)
if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
$libraries | ForEach-Object { Copy-Item -LiteralPath $_ -Destination $outputDir -Force }
Copy-Item (Join-Path $projectRoot 'src\App.config') (Join-Path $outputDir 'ST4RR.exe.config') -Force
New-Item -ItemType Directory -Path (Join-Path $outputDir 'fixtures') -Force | Out-Null
Copy-Item (Join-Path $projectRoot 'tests\fixtures\*.html') (Join-Path $outputDir 'fixtures') -Force
Write-Output "Built: $outputDir\ST4RR.exe"
