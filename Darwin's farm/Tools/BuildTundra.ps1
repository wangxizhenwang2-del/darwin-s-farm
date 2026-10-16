param(
    [string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe',
    [string]$SourceModel,
    [string]$MayaPython='C:/Program Files/Autodesk/Maya2027/bin/mayapy.exe',
    [switch]$Install
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture=Join-Path $projectRoot 'Temp/MoebiusMergeValidation'
$result=Join-Path $fixture 'Validation/TundraImport'
function Invoke-TundraUnity([string]$Method,[string]$Log){
    $arguments=@('-batchmode','-projectPath',('"'+$fixture+'"'),'-executeMethod',$Method,'-logFile',('"'+$fixture+'/'+$Log+'"'))
    $process=Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Output "Unity $Method process: $($process.Id)"
    $deadline=[DateTime]::UtcNow.AddMinutes(8)
    while(-not $process.WaitForExit(1000)){
        if([DateTime]::UtcNow -gt $deadline){throw "Unity is still running: $($process.Id). Inspect $fixture/$Log"}
    }
    if($process.ExitCode -ne 0){throw "Unity validation failed ($($process.ExitCode)): $fixture/$Log"}
}
if(-not $Install){
    if($SourceModel){
        New-Item -ItemType Directory -Force -Path "$projectRoot/Temp/TundraImport" | Out-Null
        & $MayaPython (Join-Path $PSScriptRoot 'ExportHighland.py') $SourceModel "$projectRoot/Temp/TundraImport/Tundra.fbx" "$projectRoot/Temp/TundraImport/SourceReport.json"
        if($LASTEXITCODE -ne 0){throw 'Maya FBX export failed'}
    }
    if(-not(Test-Path -LiteralPath "$projectRoot/Temp/TundraImport/Tundra.fbx")){
        throw 'First export the supplied Maya scene using Tools/ExportHighland.py (source, destination FBX, report JSON).'
    }
    . (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
    New-Item -ItemType Directory -Force -Path "$fixture/Assets/Art/ImportedTundra","$fixture/Assets/Editor","$fixture/Packages","$fixture/ProjectSettings" | Out-Null
    Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $fixture
    Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$fixture/Assets" -Recurse -Force
    Copy-Item -LiteralPath "$projectRoot/Assets/Settings.meta" -Destination "$fixture/Assets/Settings.meta" -Force
    Copy-Item -Path "$projectRoot/ProjectSettings/*" -Destination "$fixture/ProjectSettings" -Force
    Copy-Item -LiteralPath "$projectRoot/Packages/manifest.json","$projectRoot/Packages/packages-lock.json" -Destination "$fixture/Packages" -Force
    Copy-Item -LiteralPath "$projectRoot/Temp/TundraImport/Tundra.fbx" -Destination "$fixture/Assets/Art/ImportedTundra/Tundra_Rocks_v2.fbx" -Force
    Copy-Item -LiteralPath "$projectRoot/Tools/MoebiusValidation/Editor/MoebiusTundraImport.cs" -Destination "$fixture/Assets/Editor" -Force
    Set-Content -LiteralPath "$fixture/TundraImport.marker" -Value 'Guarded isolated Tundra model import'
    Invoke-TundraUnity 'Darwin.Rendering.Validation.MoebiusTundraImport.RunBatch' 'TundraImport.log'
    Get-Content -LiteralPath "$result/Report.txt" -Tail 18
    Invoke-TundraUnity 'Darwin.Rendering.Validation.MoebiusTundraImport.VerifyReload' 'TundraReload.log'
    Get-Content -LiteralPath "$result/ReloadReport.txt"
    exit
}
foreach($report in @('Report.txt','ReloadReport.txt')){
    if(-not(Test-Path -LiteralPath "$result/$report") -or -not((Get-Content -LiteralPath "$result/$report") -contains 'Errors: 0')){
        throw "Successful validation required: $result/$report"
    }
}
$outputs=@(
    'Assets/Art/ImportedTundra',
    'Assets/Rendering/Moebius/Content/Biomes/Tundra',
    'Assets/Rendering/Moebius/Samples/Biomes/Prefabs/Tundra_Colored.prefab',
    'Assets/Rendering/Moebius/Samples/Biomes/Scenes/Tundra_Colored.unity'
)
foreach($relative in $outputs){
    if(Test-Path -LiteralPath "$projectRoot/$relative"){throw "Will not overwrite an existing asset: $relative"}
}
foreach($relative in $outputs){
    $destination=Join-Path $projectRoot $relative
    $parent=Split-Path $destination -Parent
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    Copy-Item -LiteralPath "$fixture/$relative" -Destination $destination -Recurse
    Copy-Item -LiteralPath "$fixture/$relative.meta" -Destination "$destination.meta"
    # Copy Unity metadata for newly introduced intermediate folders only.
    $relativeParent=Split-Path $relative -Parent
    while($relativeParent -and $relativeParent -ne 'Assets'){
        $sourceMeta=Join-Path $fixture ($relativeParent+'.meta')
        $targetMeta=Join-Path $projectRoot ($relativeParent+'.meta')
        if((Test-Path -LiteralPath $sourceMeta) -and -not(Test-Path -LiteralPath $targetMeta)){
            Copy-Item -LiteralPath $sourceMeta -Destination $targetMeta
        }
        $relativeParent=Split-Path $relativeParent -Parent
    }
}
$artifacts=Join-Path $projectRoot 'Artifacts/MoebiusTundraImport'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
Copy-Item -Path "$result/*" -Destination $artifacts -Force
Copy-Item -LiteralPath "$projectRoot/Temp/TundraImport/SourceReport.json" -Destination "$artifacts/SourceReport.json" -Force
Write-Output 'Installed the new colored Tundra prefab, preview scene, materials and original FBX. Existing biomes were not modified.'
