param([string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
# Reuse the warmed isolated water fixture; never open or close the user's Unity Editor.
$testRoot=Join-Path $projectRoot 'Temp/MoebiusWaterValidation'
New-Item -ItemType Directory -Force -Path "$testRoot/Assets/Rendering","$testRoot/Packages","$testRoot/ProjectSettings" | Out-Null
Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $testRoot
Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$testRoot/Assets" -Recurse -Force
Copy-Item -LiteralPath "$projectRoot/ProjectSettings/ProjectVersion.txt","$projectRoot/ProjectSettings/ProjectSettings.asset" -Destination "$testRoot/ProjectSettings" -Force
Set-Content -LiteralPath "$testRoot/Packages/manifest.json" -Value '{"dependencies":{"com.unity.render-pipelines.universal":"17.2.0","com.unity.inputsystem":"1.14.2","com.unity.modules.imgui":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.terrain":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}'
Install-MoebiusValidationSource -ProjectRoot $projectRoot -ValidationPath $testRoot -SourceFile 'MoebiusWaterfallValidation.cs'
Copy-Item -LiteralPath "$projectRoot/Tools/MoebiusValidation/WaterfallWorldProbe.shader" -Destination "$testRoot/Assets/Editor" -Force
Set-Content -LiteralPath "$testRoot/WaterfallValidation.marker" -Value 'Waterfall validation'
$arguments=@('-batchmode','-projectPath',('"'+$testRoot+'"'),'-executeMethod','Darwin.Rendering.Editor.MoebiusWaterfallValidation.RunBatch','-logFile',('"'+$testRoot+'/Waterfall.log"'))
$process=Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Waterfall validation started: PID $($process.Id)"
while(-not $process.WaitForExit(10000)){}
$output=Join-Path $projectRoot 'Artifacts/MoebiusWaterfall'
New-Item -ItemType Directory -Force -Path $output | Out-Null
if(Test-Path "$testRoot/Validation/Waterfall"){Copy-Item -Path "$testRoot/Validation/Waterfall/*" -Destination $output -Force}
if($process.ExitCode -ne 0){Get-Content "$testRoot/Waterfall.log" -Tail 30;Get-Content "$output/Report.txt" -ErrorAction SilentlyContinue;throw 'Waterfall validation failed.'}
Get-Content "$output/Report.txt"
