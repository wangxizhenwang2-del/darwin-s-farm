param(
    [string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe',
    [string]$FixtureName='MoebiusBiomeIdentityValidation',
    [switch]$ApplyExisting
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if($FixtureName -notmatch '^Moebius[A-Za-z0-9]+Validation$'){throw 'Invalid isolated fixture name'}
$fixture=Join-Path $projectRoot ('Temp/'+$FixtureName)
$targets=@(
    @{Name='Grassland';Scene='Assets/Rendering/Moebius/Samples/Biomes/Scenes/Grassland_Moebius.unity'},
    @{Name='Forest';Scene='Assets/Rendering/Moebius/Samples/Biomes/Scenes/Forest_Moebius.unity'},
    @{Name='Rainforest';Scene='Assets/Rendering/Moebius/Samples/Biomes/Scenes/RainForest.unity'}
)
function Invoke-BiomeIdentityUnity([string]$Method,[string]$Log){
    $arguments=@('-batchmode','-projectPath',('"'+$fixture+'"'),'-executeMethod',$Method,'-logFile',('"'+$fixture+'/'+$Log+'"'))
    $process=Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Output "Unity $Method process: $($process.Id)"
    $deadline=[DateTime]::UtcNow.AddMinutes(8)
    while(-not $process.WaitForExit(1000)){
        if([DateTime]::UtcNow -gt $deadline){throw "Unity is still running: $($process.Id). Inspect $fixture/$Log"}
    }
    if($process.ExitCode -ne 0){throw "Unity validation failed ($($process.ExitCode)): $fixture/$Log"}
}
if(-not $ApplyExisting){
    . (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
    New-Item -ItemType Directory -Force -Path "$fixture/Assets/Art","$fixture/Assets/Rendering","$fixture/Assets/Editor","$fixture/Packages","$fixture/ProjectSettings" | Out-Null
    foreach($target in $targets){
        (Get-FileHash -LiteralPath (Join-Path $projectRoot $target.Scene)).Hash | Set-Content "$fixture/Source$($target.Name)SceneHash.txt"
    }
    Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $fixture
    Copy-Item -LiteralPath "$projectRoot/Assets/Art/ImportedBiomes" -Destination "$fixture/Assets/Art" -Recurse -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Art/ImportedBiomes.meta" -Destination "$fixture/Assets/Art/ImportedBiomes.meta" -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$fixture/Assets" -Recurse -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Settings.meta" -Destination "$fixture/Assets/Settings.meta" -Force
    Copy-Item -Path "$projectRoot/ProjectSettings/*" -Destination "$fixture/ProjectSettings" -Force
    Copy-Item -LiteralPath "$projectRoot/Packages/manifest.json","$projectRoot/Packages/packages-lock.json" -Destination "$fixture/Packages" -Force
    foreach($name in @('MoebiusBiomeVFXIdentity.cs','MoebiusBiomeVFXIdentityPlay.cs')){
        Copy-Item -LiteralPath "$projectRoot/Tools/MoebiusValidation/Editor/$name" -Destination "$fixture/Assets/Editor" -Force
    }
    Set-Content -LiteralPath "$fixture/BiomeVFXIdentity.marker" -Value 'Guarded isolated biome VFX identity update'
    foreach($target in $targets){
        $name=$target.Name
        Set-Content -LiteralPath "$fixture/BiomeVFXIdentityTarget.txt" -Value $name
        Invoke-BiomeIdentityUnity "Darwin.Rendering.Validation.MoebiusBiomeVFXIdentity.RunBatch" "$($name)VFX.log"
        Get-Content -LiteralPath "$fixture/Validation/BiomeVFXIdentity/$name/Report.txt"
        Invoke-BiomeIdentityUnity 'Darwin.Rendering.Validation.MoebiusBiomeVFXIdentityPlay.RunBatch' "$($name)VFXPlay.log"
        Get-Content -LiteralPath "$fixture/Validation/BiomeVFXIdentity/$name/RuntimeReport.txt"
    }
    Write-Output 'Review the three biome comparison captures; install with -ApplyExisting.'
    exit
}
# Validate all three results before changing source scenes.
foreach($target in $targets){
    $name=$target.Name
    foreach($report in @('Report.txt','RuntimeReport.txt')){
        $path="$fixture/Validation/BiomeVFXIdentity/$name/$report"
        if(-not(Test-Path -LiteralPath $path) -or -not((Get-Content -LiteralPath $path) -contains 'Errors: 0')){throw "Successful validation required: $path"}
    }
    if((Get-FileHash -LiteralPath (Join-Path $projectRoot $target.Scene)).Hash -ne (Get-Content -LiteralPath "$fixture/Source$($name)SceneHash.txt" -Raw).Trim()){
        throw "$name scene changed during validation. Preserve current edits and rebuild."
    }
}
foreach($target in $targets){
    $name=$target.Name
    $asset='Assets/Rendering/Moebius/Content/VFX/'+$name
    Copy-Item -LiteralPath "$fixture/$asset" -Destination "$projectRoot/Assets/Rendering/Moebius/Content/VFX" -Recurse -Force
    Copy-Item -LiteralPath "$fixture/$asset.meta" -Destination "$projectRoot/$asset.meta" -Force
    Copy-Item -LiteralPath (Join-Path $fixture $target.Scene) -Destination (Join-Path $projectRoot $target.Scene) -Force
    $artifacts=Join-Path $projectRoot ('Artifacts/MoebiusBiomeVFXIdentity/'+$name)
    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
    foreach($file in @('Before.png','Frame359.png','Frame599.png','Frame899.png','MainCamera.png','EnvironmentBefore.png','IntensityZero.png','Report.txt','PlayMode.png','Orthographic.png','RuntimeReport.txt')){
        $source="$fixture/Validation/BiomeVFXIdentity/$name/$file"
        if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $artifacts -Force}
    }
}
Write-Output 'Installed distinct Grassland, Forest and Rainforest VFX. Environment assets and other biomes were preserved.'
