param(
    [string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe',
    [switch]$ApplyExisting
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture=Join-Path $projectRoot 'Temp/MoebiusHighlandVFXValidation'
$scene='Assets/Rendering/Moebius/Samples/Biomes/Scenes/Highland_Animals.unity'
$report=Join-Path $fixture 'Validation/HighlandVFX/Report.txt'
if(-not $ApplyExisting){
    . (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
    New-Item -ItemType Directory -Force -Path "$fixture/Assets/Art","$fixture/Assets/Rendering","$fixture/Assets/Editor","$fixture/Packages","$fixture/ProjectSettings" | Out-Null
    (Get-FileHash -LiteralPath (Join-Path $projectRoot $scene)).Hash | Set-Content "$fixture/SourceSceneHash.txt"
    Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $fixture
    Copy-Item -LiteralPath "$projectRoot/Assets/Art/ImportedBiomes" -Destination "$fixture/Assets/Art" -Recurse -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Art/ImportedBiomes.meta" -Destination "$fixture/Assets/Art/ImportedBiomes.meta" -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Art/ImportedHighland" -Destination "$fixture/Assets/Art" -Recurse -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Art/ImportedHighland.meta" -Destination "$fixture/Assets/Art/ImportedHighland.meta" -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Art/Animals" -Destination "$fixture/Assets/Art" -Recurse -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Art/Animals.meta" -Destination "$fixture/Assets/Art/Animals.meta" -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$fixture/Assets" -Recurse -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Settings.meta" -Destination "$fixture/Assets/Settings.meta" -Force
    Copy-Item -Path "$projectRoot/ProjectSettings/*" -Destination "$fixture/ProjectSettings" -Force
    Copy-Item -LiteralPath "$projectRoot/Packages/manifest.json","$projectRoot/Packages/packages-lock.json" -Destination "$fixture/Packages" -Force
    Copy-Item -LiteralPath "$projectRoot/Tools/MoebiusValidation/Editor/MoebiusHighlandVFXSetup.cs" -Destination "$fixture/Assets/Editor" -Force
    Copy-Item -LiteralPath "$projectRoot/Tools/MoebiusValidation/Editor/MoebiusHighlandVFXPlayValidation.cs" -Destination "$fixture/Assets/Editor" -Force
    Set-Content -LiteralPath "$fixture/HighlandVFX.marker" -Value 'Guarded isolated Highland VFX authoring'
    $unityArguments=@('-batchmode','-projectPath',('"'+$fixture+'"'),'-executeMethod','Darwin.Rendering.Validation.MoebiusHighlandVFXSetup.RunBatch','-logFile',('"'+$fixture+'/HighlandVFX.log"'))
    $process=Start-Process -FilePath $UnityPath -ArgumentList $unityArguments -WindowStyle Hidden -PassThru
    Write-Output "Unity VFX authoring process: $($process.Id)"
    $deadline=[DateTime]::UtcNow.AddMinutes(8)
    while(-not $process.WaitForExit(1000)){
        if([DateTime]::UtcNow -gt $deadline){throw "Unity is still running: $($process.Id). Inspect $fixture/HighlandVFX.log"}
    }
    if(Test-Path -LiteralPath $report){Get-Content -LiteralPath $report}
    if($process.ExitCode -ne 0){throw "VFX authoring failed ($($process.ExitCode)). Inspect $fixture/HighlandVFX.log"}
    $playArguments=@('-batchmode','-projectPath',('"'+$fixture+'"'),'-executeMethod','Darwin.Rendering.Validation.MoebiusHighlandVFXPlayValidation.RunBatch','-logFile',('"'+$fixture+'/HighlandVFXPlay.log"'))
    $playProcess=Start-Process -FilePath $UnityPath -ArgumentList $playArguments -WindowStyle Hidden -PassThru
    $playDeadline=[DateTime]::UtcNow.AddMinutes(3)
    while(-not $playProcess.WaitForExit(1000)){
        if([DateTime]::UtcNow -gt $playDeadline){throw "Play validation is still running: $($playProcess.Id). Inspect $fixture/HighlandVFXPlay.log"}
    }
    if($playProcess.ExitCode -ne 0){throw "Native Play Mode validation failed. Inspect $fixture/HighlandVFXPlay.log"}
    Get-Content -LiteralPath "$fixture/Validation/HighlandVFX/RuntimeReport.txt"
    Write-Output 'Review the Validation/HighlandVFX screenshots, then use -ApplyExisting to install the verified assets.'
    exit
}
if(-not (Test-Path -LiteralPath $report) -or -not ((Get-Content -LiteralPath $report) -contains 'Errors: 0')){throw 'A successful isolated build is required before applying.'}
if(-not (Test-Path -LiteralPath "$fixture/Validation/HighlandVFX/RuntimeReport.txt") -or -not ((Get-Content -LiteralPath "$fixture/Validation/HighlandVFX/RuntimeReport.txt") -contains 'Errors: 0')){throw 'A successful native Play Mode check is required before applying.'}
if((Get-FileHash -LiteralPath (Join-Path $projectRoot $scene)).Hash -ne (Get-Content "$fixture/SourceSceneHash.txt" -Raw).Trim()){
    throw 'The source Highland scene changed during validation. Keep the fixture and rebuild from the current scene.'
}
$asset='Assets/Rendering/Moebius/Content/VFX'
$destination=Join-Path $projectRoot $asset
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -LiteralPath "$fixture/$asset/Highland" -Destination $destination -Recurse -Force
Copy-Item -LiteralPath "$fixture/$asset/Highland.meta" -Destination "$destination/Highland.meta" -Force
Copy-Item -LiteralPath "$fixture/$asset.meta" -Destination "$projectRoot/$asset.meta" -Force
foreach($file in @(
    'Assets/Rendering/Moebius/Runtime/Controllers/MoebiusWindController.cs',
    'Assets/Rendering/Moebius/Shaders/Materials/MoebiusDust.shader',
    'Assets/Rendering/Moebius/Shaders/Materials/MoebiusDebris.shader',
    'Assets/Rendering/Moebius/Shaders/Materials/MoebiusWind.shader',
    'Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusParticleInput.hlsl',
    'Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusParticleCommon.hlsl'
)){
    Copy-Item -LiteralPath "$fixture/$file.meta" -Destination "$projectRoot/$file.meta" -Force
}
Copy-Item -LiteralPath "$fixture/$scene" -Destination "$projectRoot/$scene" -Force
$artifacts=Join-Path $projectRoot 'Artifacts/MoebiusHighlandVFX'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
foreach($file in @('Before.png','Frame239.png','Frame479.png','Frame719.png','IntensityZero.png','Report.txt','PlayMode.png','Orthographic.png','RuntimeReport.txt')){
    $source=Join-Path "$fixture/Validation/HighlandVFX" $file
    if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $artifacts -Force}
}
Write-Output 'Installed Highland VFX assets and the verified scene. Existing scene meta, biome prefab and paint assets were preserved.'
