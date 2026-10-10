param([string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
$testRoot=Join-Path $projectRoot 'Temp/MoebiusBillboardValidation'
New-Item -ItemType Directory -Force -Path "$testRoot/Assets/Art", "$testRoot/Assets/Rendering", "$testRoot/Packages", "$testRoot/ProjectSettings" | Out-Null
Copy-Item -LiteralPath "$projectRoot/Assets/Art/ImportedBiomes" -Destination "$testRoot/Assets/Art" -Recurse -Force
Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $testRoot
Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$testRoot/Assets" -Recurse -Force
Copy-Item -Path "$projectRoot/ProjectSettings/*" -Destination "$testRoot/ProjectSettings" -Force
Copy-Item -LiteralPath "$projectRoot/Packages/manifest.json","$projectRoot/Packages/packages-lock.json" -Destination "$testRoot/Packages" -Force
Install-MoebiusValidationSource -ProjectRoot $projectRoot -ValidationPath $testRoot -SourceFile 'MoebiusBiomePreviewBuilder.cs'
Set-Content -LiteralPath "$testRoot/BiomeValidation.marker" -Value 'Isolated model preview build'
$arguments=@('-batchmode','-projectPath',('"'+$testRoot+'"'),'-executeMethod','Darwin.Rendering.Editor.MoebiusBiomePreviewBuilder.RunBatch','-logFile',('"'+$testRoot+'/BiomeValidation.log"'))
$process=Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(300000)) { throw "Unity is still running: $($process.Id); inspect $testRoot/BiomeValidation.log" }
Get-Content -LiteralPath "$testRoot/Validation/Report.txt"
if($process.ExitCode -ne 0) { throw "Model validation failed: $($process.ExitCode); inspect $testRoot/BiomeValidation.log" }
