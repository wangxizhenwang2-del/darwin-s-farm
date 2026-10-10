param([string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
$testRoot=Join-Path $projectRoot 'Temp/HighlandPaintValidation'
New-Item -ItemType Directory -Force -Path "$testRoot/Assets/Art","$testRoot/Assets/Rendering","$testRoot/Packages","$testRoot/ProjectSettings" | Out-Null
Copy-Item -LiteralPath "$projectRoot/Assets/Art/ImportedHighland" -Destination "$testRoot/Assets/Art" -Recurse -Force
Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $testRoot
Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$testRoot/Assets" -Recurse -Force
Copy-Item -Path "$projectRoot/ProjectSettings/*" -Destination "$testRoot/ProjectSettings" -Force
Copy-Item -LiteralPath "$projectRoot/Packages/manifest.json","$projectRoot/Packages/packages-lock.json" -Destination "$testRoot/Packages" -Force
Install-MoebiusValidationSource -ProjectRoot $projectRoot -ValidationPath $testRoot -SourceFile 'HighlandPaintValidation.cs'
Set-Content -LiteralPath "$testRoot/HighlandPaint.marker" -Value 'Isolated Highland model import and paint test'
$unityArguments=@('-batchmode','-projectPath',('"'+$testRoot+'"'),'-executeMethod','Darwin.Rendering.Editor.Paint.HighlandPaintValidation.RunBatch','-logFile',('"'+$testRoot+'/HighlandPaint.log"'))
$process=Start-Process -FilePath $UnityPath -ArgumentList $unityArguments -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(300000)){throw "Unity validation still running: $($process.Id). Inspect $testRoot/HighlandPaint.log"}
Get-Content -LiteralPath "$testRoot/Validation/HighlandPaint/Report.txt" -ErrorAction SilentlyContinue
if($process.ExitCode -ne 0){Get-Content -LiteralPath "$testRoot/HighlandPaint.log" -Tail 30;throw "Highland paint validation failed: $($process.ExitCode)"}
