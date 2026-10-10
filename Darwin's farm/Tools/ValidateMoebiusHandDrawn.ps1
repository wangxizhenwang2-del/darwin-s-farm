param([ValidateSet('Before','After','DynamicBefore','DynamicAfter')][string]$Label='After',[string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
$testRoot=Join-Path $projectRoot 'Temp/MoebiusBillboardValidation'
New-Item -ItemType Directory -Force -Path "$testRoot/Assets/Rendering","$testRoot/Assets/Art","$testRoot/Packages","$testRoot/ProjectSettings" | Out-Null
Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $testRoot
Copy-Item -LiteralPath "$projectRoot/Assets/Art/ImportedBiomes" -Destination "$testRoot/Assets/Art" -Recurse -Force
Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$testRoot/Assets" -Recurse -Force
Copy-Item -Path "$projectRoot/ProjectSettings/*" -Destination "$testRoot/ProjectSettings" -Force
Copy-Item -LiteralPath "$projectRoot/Packages/manifest.json","$projectRoot/Packages/packages-lock.json" -Destination "$testRoot/Packages" -Force
Install-MoebiusValidationSource -ProjectRoot $projectRoot -ValidationPath $testRoot -SourceFile 'MoebiusHandDrawnValidation.cs'
Set-Content -LiteralPath "$testRoot/HandDrawnValidation.marker" -Value $Label
$handDrawnUnityArguments=@('-batchmode','-projectPath',('"'+$testRoot+'"'),'-executeMethod','Darwin.Rendering.Editor.MoebiusHandDrawnValidation.RunBatch','-logFile',('"'+$testRoot+'/HandDrawnValidation.log"'))
$process=Start-Process -FilePath $UnityPath -ArgumentList $handDrawnUnityArguments -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(300000)){throw "Unity still running: $($process.Id)"}
if($process.ExitCode -ne 0){Get-Content "$testRoot/HandDrawnValidation.log" -Tail 20;throw 'Hand-drawn validation failed.'}
Get-Content "$testRoot/Validation/HandDrawn/$Label/Report.txt"
