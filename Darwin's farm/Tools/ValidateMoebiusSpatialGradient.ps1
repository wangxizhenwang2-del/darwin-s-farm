param([ValidateSet('Before','After')][string]$Label='After',[string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
$testRoot=Join-Path $projectRoot 'Temp/MoebiusBillboardValidation'
New-Item -ItemType Directory -Force -Path "$testRoot/Assets/Rendering","$testRoot/Assets/Scenes","$testRoot/Packages","$testRoot/ProjectSettings" | Out-Null
Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $testRoot
Copy-Item -LiteralPath "$projectRoot/Assets/Scenes/MoebiusComicDemo.unity","$projectRoot/Assets/Scenes/MoebiusComicDemo.unity.meta" -Destination "$testRoot/Assets/Scenes" -Force
# Repeatable scene fixture: the delivered source scene already contains the saved test group.
Copy-Item -LiteralPath "$projectRoot/Artifacts/MoebiusSpatialGradient/SourceBefore/MoebiusComicDemo.unity" -Destination "$testRoot/Assets/Scenes/MoebiusComicDemo.unity" -Force
if($Label -eq 'Before'){
    Install-MoebiusShaderBaseline -ProjectRoot $projectRoot -ValidationPath $testRoot -SourceFolder "$projectRoot/Artifacts/MoebiusSpatialGradient/SourceBefore"
}
Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$testRoot/Assets" -Recurse -Force
Copy-Item -Path "$projectRoot/ProjectSettings/*" -Destination "$testRoot/ProjectSettings" -Force
Copy-Item -LiteralPath "$projectRoot/Packages/manifest.json","$projectRoot/Packages/packages-lock.json" -Destination "$testRoot/Packages" -Force
Install-MoebiusValidationSource -ProjectRoot $projectRoot -ValidationPath $testRoot -SourceFile 'MoebiusSpatialGradientValidation.cs'
Set-Content -LiteralPath "$testRoot/SpatialGradientValidation.marker" -Value $Label
$spatialUnityArgs=@('-batchmode','-projectPath',('"'+$testRoot+'"'),'-executeMethod','Darwin.Rendering.Editor.MoebiusSpatialGradientValidation.RunBatch','-logFile',('"'+$testRoot+'/SpatialGradientValidation.log"'))
$process=Start-Process -FilePath $UnityPath -ArgumentList $spatialUnityArgs -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(300000)){throw "Validation Unity still running: $($process.Id)"}
Get-Content "$testRoot/Validation/SpatialGradient/$Label/Report.txt" -ErrorAction SilentlyContinue
if($process.ExitCode -ne 0){Get-Content "$testRoot/SpatialGradientValidation.log" -Tail 15;throw 'Spatial gradient validation failed.'}
