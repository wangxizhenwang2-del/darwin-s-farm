param([ValidateSet('Before','After','Reload','Sphere')][string]$Label='After',[string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
$testRoot=Join-Path $projectRoot 'Temp/MoebiusBillboardValidation'
New-Item -ItemType Directory -Force -Path "$testRoot/Assets/Rendering","$testRoot/Packages","$testRoot/ProjectSettings" | Out-Null
if($Label -ne 'Reload'){
    Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $testRoot
    if($Label -eq 'Before'){
        Install-MoebiusShaderBaseline -ProjectRoot $projectRoot -ValidationPath $testRoot -SourceFolder "$projectRoot/Artifacts/MoebiusPaint/SourceBefore"
    }
    Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$testRoot/Assets" -Recurse -Force
    Copy-Item -Path "$projectRoot/ProjectSettings/*" -Destination "$testRoot/ProjectSettings" -Force
    Copy-Item -LiteralPath "$projectRoot/Packages/manifest.json","$projectRoot/Packages/packages-lock.json" -Destination "$testRoot/Packages" -Force
}
# Reload must reopen the project saved by After, including its newly generated demo scene and materials.
Install-MoebiusValidationSource -ProjectRoot $projectRoot -ValidationPath $testRoot -SourceFile 'MoebiusPaintValidation.cs'
Set-Content -LiteralPath "$testRoot/PaintValidation.marker" -Value $Label
$paintArgs=@('-batchmode','-projectPath',('"'+$testRoot+'"'),'-executeMethod','Darwin.Rendering.Editor.Paint.MoebiusPaintValidation.RunBatch','-logFile',('"'+$testRoot+'/PaintValidation.log"'))
$process=Start-Process -FilePath $UnityPath -ArgumentList $paintArgs -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(300000)){throw "Paint validation Unity still running: $($process.Id)"}
Get-Content "$testRoot/Validation/Paint/$Label/Report.txt" -ErrorAction SilentlyContinue
if($process.ExitCode -ne 0){Get-Content "$testRoot/PaintValidation.log" -Tail 15;throw 'Moebius Paint validation failed.'}
