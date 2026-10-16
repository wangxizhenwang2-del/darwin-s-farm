param(
    [string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe',
    [string]$FixtureName='MoebiusWoodlandVFXValidation',
    [switch]$ApplyExisting
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if($FixtureName -notmatch '^Moebius[A-Za-z0-9]+Validation$'){throw 'Invalid isolated fixture name'}
$fixture=Join-Path $projectRoot ('Temp/'+$FixtureName)
$targets=@(
    @{Name='Forest';Scene='Assets/Rendering/Moebius/Samples/Biomes/Scenes/Forest_Moebius.unity'},
    @{Name='Rainforest';Scene='Assets/Rendering/Moebius/Samples/Biomes/Scenes/RainForest.unity'}
)
function Invoke-WoodlandUnity([string]$Method,[string]$Log){
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
    foreach($name in @('MoebiusWoodlandVFXSetup.cs','MoebiusWoodlandVFXPlayValidation.cs')){
        Copy-Item -LiteralPath "$projectRoot/Tools/MoebiusValidation/Editor/$name" -Destination "$fixture/Assets/Editor" -Force
    }
    Set-Content -LiteralPath "$fixture/WoodlandVFX.marker" -Value 'Guarded isolated woodland VFX authoring'
    foreach($target in $targets){
        $name=$target.Name
        Set-Content -LiteralPath "$fixture/WoodlandTarget.txt" -Value $name
        Invoke-WoodlandUnity "Darwin.Rendering.Validation.MoebiusWoodlandVFXSetup.Run$($name)Batch" "$($name)VFX.log"
        Get-Content -LiteralPath "$fixture/Validation/$($name)VFX/Report.txt"
        Invoke-WoodlandUnity 'Darwin.Rendering.Validation.MoebiusWoodlandVFXPlayValidation.RunBatch' "$($name)VFXPlay.log"
        Get-Content -LiteralPath "$fixture/Validation/$($name)VFX/RuntimeReport.txt"
    }
    Write-Output 'Review both validated scene captures; install with -ApplyExisting.'
    exit
}
# Validate both results before changing either source scene.
foreach($target in $targets){
    $name=$target.Name
    foreach($report in @('Report.txt','RuntimeReport.txt')){
        $path="$fixture/Validation/$($name)VFX/$report"
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
    $artifacts=Join-Path $projectRoot ('Artifacts/Moebius'+$name+'VFX')
    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
    foreach($file in @('Before.png','Frame239.png','Frame539.png','Frame899.png','Overview.png','Closeup.png','IntensityZero.png','Report.txt','PlayMode.png','Orthographic.png','RuntimeReport.txt')){
        $source="$fixture/Validation/$($name)VFX/$file"
        if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $artifacts -Force}
    }
}
Write-Output 'Installed Forest and Rainforest VFX. Shared particle assets and other scenes were preserved.'
