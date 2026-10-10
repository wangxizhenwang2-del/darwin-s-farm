param([string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe', [switch]$MotionPreview, [switch]$SmoothPreview, [switch]$AutoPreview)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
$testRoot=Join-Path $projectRoot 'Temp/MoebiusWaterValidation'
New-Item -ItemType Directory -Force -Path "$testRoot/Assets/Rendering","$testRoot/Assets/Editor","$testRoot/Packages","$testRoot/ProjectSettings" | Out-Null
Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $testRoot
Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$testRoot/Assets" -Recurse -Force
Copy-Item -LiteralPath "$projectRoot/ProjectSettings/ProjectVersion.txt","$projectRoot/ProjectSettings/ProjectSettings.asset" -Destination "$testRoot/ProjectSettings" -Force
Set-Content -LiteralPath "$testRoot/Packages/manifest.json" -Value '{"dependencies":{"com.unity.render-pipelines.universal":"17.2.0","com.unity.inputsystem":"1.14.2","com.unity.modules.imgui":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.terrain":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}'
Install-MoebiusValidationSource -ProjectRoot $projectRoot -ValidationPath $testRoot -SourceFile 'MoebiusWaterValidation.cs'
Copy-Item -LiteralPath "$projectRoot/Tools/MoebiusValidation/WaterValidationDepth.shader" -Destination "$testRoot/Assets/Editor" -Force
Set-Content -LiteralPath "$testRoot/WaterValidation.marker" -Value 'Water validation'
$method=if($AutoPreview){'Darwin.Rendering.Editor.MoebiusWaterValidation.RunAutomaticWaterBatch'}elseif($SmoothPreview){'Darwin.Rendering.Editor.MoebiusWaterValidation.RunSmoothPreviewBatch'}elseif($MotionPreview){'Darwin.Rendering.Editor.MoebiusWaterValidation.RunMotionPreviewBatch'}else{'Darwin.Rendering.Editor.MoebiusWaterValidation.RunBatch'}
$report=if($AutoPreview){'AutomaticWaterReport.txt'}elseif($SmoothPreview){'SmoothMotionReport.txt'}elseif($MotionPreview){'MotionReport.txt'}else{'Report.txt'}
$arguments=@('-batchmode','-projectPath',('"'+$testRoot+'"'),'-executeMethod',$method,'-logFile',('"'+$testRoot+'/Water.log"'))
$process=Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Water validation started: PID $($process.Id)"
while(-not $process.WaitForExit(10000)){}
if($process.ExitCode -ne 0){Get-Content "$testRoot/Water.log" -Tail 40; Get-Content "$testRoot/Validation/Water/$report" -ErrorAction SilentlyContinue; throw 'Water validation failed.'}
$output=Join-Path $projectRoot 'Artifacts/MoebiusWater'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -Path "$testRoot/Validation/Water/*" -Destination $output -Force
Get-Content "$output/$report"
Write-Output 'Demo assets remain isolated. Review Delivery.txt (or AutomaticDelivery.txt with -AutoPreview) before copying listed assets and their metadata.'
