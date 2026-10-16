param(
    [string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe',
    [string]$FixtureName="MoebiusMergeValidation",
    [switch]$ApplyExisting
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if($FixtureName -notmatch '^Moebius[A-Za-z0-9]+Validation$'){throw 'Invalid isolated fixture name'}
$fixture=Join-Path $projectRoot ('Temp/'+$FixtureName)
$scene='Assets/Rendering/Moebius/Samples/Biomes/Scenes/Tundra_Colored.unity'
$report=Join-Path $fixture 'Validation/TundraVFX/Report.txt'
if(-not $ApplyExisting){
    . (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
    New-Item -ItemType Directory -Force -Path "$fixture/Assets/Art","$fixture/Assets/Rendering","$fixture/Assets/Editor","$fixture/Packages","$fixture/ProjectSettings" | Out-Null
    (Get-FileHash -LiteralPath (Join-Path $projectRoot $scene)).Hash | Set-Content "$fixture/SourceSceneHash.txt"
    Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $fixture
    Copy-Item -LiteralPath "$projectRoot/Assets/Art/ImportedTundra" -Destination "$fixture/Assets/Art" -Recurse -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Art/ImportedTundra.meta" -Destination "$fixture/Assets/Art/ImportedTundra.meta" -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$fixture/Assets" -Recurse -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Settings.meta" -Destination "$fixture/Assets/Settings.meta" -Force
    Copy-Item -Path "$projectRoot/ProjectSettings/*" -Destination "$fixture/ProjectSettings" -Force
    Copy-Item -LiteralPath "$projectRoot/Packages/manifest.json","$projectRoot/Packages/packages-lock.json" -Destination "$fixture/Packages" -Force
    Copy-Item -LiteralPath "$projectRoot/Tools/MoebiusValidation/Editor/MoebiusTundraVFXSetup.cs" -Destination "$fixture/Assets/Editor" -Force
    Copy-Item -LiteralPath "$projectRoot/Tools/MoebiusValidation/Editor/MoebiusTundraVFXPlayValidation.cs" -Destination "$fixture/Assets/Editor" -Force
    Set-Content -LiteralPath "$fixture/TundraVFX.marker" -Value 'Guarded isolated Tundra VFX authoring'
    $unityArguments=@('-batchmode','-projectPath',('"'+$fixture+'"'),'-executeMethod','Darwin.Rendering.Validation.MoebiusTundraVFXSetup.RunBatch','-logFile',('"'+$fixture+'/TundraVFX.log"'))
    $process=Start-Process -FilePath $UnityPath -ArgumentList $unityArguments -WindowStyle Hidden -PassThru
    Write-Output "Unity VFX authoring process: $($process.Id)"
    $deadline=[DateTime]::UtcNow.AddMinutes(8)
    while(-not $process.WaitForExit(1000)){
        if([DateTime]::UtcNow -gt $deadline){throw "Unity is still running: $($process.Id). Inspect $fixture/TundraVFX.log"}
    }
    if(Test-Path -LiteralPath $report){Get-Content -LiteralPath $report}
    if($process.ExitCode -ne 0){throw "VFX authoring failed ($($process.ExitCode)). Inspect $fixture/TundraVFX.log"}
    $playArguments=@('-batchmode','-projectPath',('"'+$fixture+'"'),'-executeMethod','Darwin.Rendering.Validation.MoebiusTundraVFXPlayValidation.RunBatch','-logFile',('"'+$fixture+'/TundraVFXPlay.log"'))
    $playProcess=Start-Process -FilePath $UnityPath -ArgumentList $playArguments -WindowStyle Hidden -PassThru
    $playDeadline=[DateTime]::UtcNow.AddMinutes(3)
    while(-not $playProcess.WaitForExit(1000)){
        if([DateTime]::UtcNow -gt $playDeadline){throw "Play validation is still running: $($playProcess.Id). Inspect $fixture/TundraVFXPlay.log"}
    }
    if($playProcess.ExitCode -ne 0){throw "Native Play Mode validation failed. Inspect $fixture/TundraVFXPlay.log"}
    Get-Content -LiteralPath "$fixture/Validation/TundraVFX/RuntimeReport.txt"
    Write-Output 'Review the Validation/TundraVFX screenshots, then use -ApplyExisting to install the verified assets.'
    exit
}
if(-not (Test-Path -LiteralPath $report) -or -not ((Get-Content -LiteralPath $report) -contains 'Errors: 0')){throw 'A successful isolated build is required before applying.'}
if(-not (Test-Path -LiteralPath "$fixture/Validation/TundraVFX/RuntimeReport.txt") -or -not ((Get-Content -LiteralPath "$fixture/Validation/TundraVFX/RuntimeReport.txt") -contains 'Errors: 0')){throw 'A successful native Play Mode check is required before applying.'}
if((Get-FileHash -LiteralPath (Join-Path $projectRoot $scene)).Hash -ne (Get-Content "$fixture/SourceSceneHash.txt" -Raw).Trim()){
    throw 'The source Tundra scene changed during validation. Keep the fixture and rebuild from the current scene.'
}
$asset='Assets/Rendering/Moebius/Content/VFX'
$destination=Join-Path $projectRoot $asset
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -LiteralPath "$fixture/$asset/Tundra" -Destination $destination -Recurse -Force
Copy-Item -LiteralPath "$fixture/$asset/Tundra.meta" -Destination "$destination/Tundra.meta" -Force

Copy-Item -LiteralPath "$fixture/$scene" -Destination "$projectRoot/$scene" -Force
$artifacts=Join-Path $projectRoot 'Artifacts/MoebiusTundraVFX'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
foreach($file in @('Before.png','Frame239.png','Frame539.png','Frame899.png','Overview.png','Closeup.png','ZeroReference.png','IntensityZero.png','Report.txt','PlayMode.png','Orthographic.png','RuntimeReport.txt')){
    $source=Join-Path "$fixture/Validation/TundraVFX" $file
    if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $artifacts -Force}
}
Write-Output 'Installed Tundra VFX assets and the verified scene. Existing scene meta, biome prefab and paint assets were preserved.'
