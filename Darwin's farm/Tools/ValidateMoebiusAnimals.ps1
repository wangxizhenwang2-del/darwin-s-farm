param([string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
$testRoot=Join-Path $projectRoot 'Temp/RenderingLayoutValidation'
New-Item -ItemType Directory -Force -Path "$testRoot/Assets/Rendering","$testRoot/Assets/Art","$testRoot/Packages","$testRoot/ProjectSettings" | Out-Null
Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $testRoot
Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$testRoot/Assets" -Recurse -Force
Copy-Item -LiteralPath "$projectRoot/Assets/Art/Animals","$projectRoot/Assets/Art/ImportedHighland" -Destination "$testRoot/Assets/Art" -Recurse -Force
Copy-Item -LiteralPath "$projectRoot/ProjectSettings/ProjectVersion.txt" -Destination "$testRoot/ProjectSettings" -Force
Set-Content -LiteralPath "$testRoot/Packages/manifest.json" -Value '{"dependencies":{"com.unity.render-pipelines.universal":"17.2.0"}}'
Install-MoebiusValidationSource -ProjectRoot $projectRoot -ValidationPath $testRoot -SourceFile 'MoebiusAnimalRenderingValidation.cs'
Set-Content -LiteralPath "$testRoot/AnimalRendering.marker" -Value 'Animal rendering validation'
$arguments=@('-batchmode','-projectPath',('"'+$testRoot+'"'),'-executeMethod','Darwin.Rendering.Editor.MoebiusAnimalRenderingValidation.RunBatch','-logFile',('"'+$testRoot+'/AnimalRendering.log"'))
$process=Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
while(-not $process.WaitForExit(10000)){}
if($process.ExitCode -ne 0){Get-Content "$testRoot/AnimalRendering.log" -Tail 30; Get-Content "$testRoot/Validation/AnimalRendering/Report.txt" -ErrorAction SilentlyContinue; throw 'Animal rendering validation failed.'}
$output=Join-Path $projectRoot 'Artifacts/AnimalRendering'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -Path "$testRoot/Validation/AnimalRendering/*" -Destination $output -Force
Get-Content "$output/Report.txt"
