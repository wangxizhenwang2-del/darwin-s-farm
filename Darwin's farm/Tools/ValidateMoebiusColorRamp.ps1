param([ValidateSet('Before','After')][string]$Label='After',[string]$UnityPath='C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
$testRoot=Join-Path $projectRoot 'Temp/MoebiusBillboardValidation'
New-Item -ItemType Directory -Force -Path "$testRoot/Assets/Rendering","$testRoot/Packages","$testRoot/ProjectSettings" | Out-Null
Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $testRoot
if($Label -eq 'Before'){
    # The captured source is the actual pre-change shader, even when re-running validation later.
    Install-MoebiusShaderBaseline -ProjectRoot $projectRoot -ValidationPath $testRoot -SourceFolder "$projectRoot/Artifacts/MoebiusColorRamp/SourceBefore"
}
Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$testRoot/Assets" -Recurse -Force
Copy-Item -Path "$projectRoot/ProjectSettings/*" -Destination "$testRoot/ProjectSettings" -Force
Copy-Item -LiteralPath "$projectRoot/Packages/manifest.json","$projectRoot/Packages/packages-lock.json" -Destination "$testRoot/Packages" -Force
Install-MoebiusValidationSource -ProjectRoot $projectRoot -ValidationPath $testRoot -SourceFile 'MoebiusColorRampValidation.cs'
Set-Content -LiteralPath "$testRoot/ColorRampValidation.marker" -Value $Label
$colorRampUnityArguments=@('-batchmode','-projectPath',('"'+$testRoot+'"'),'-executeMethod','Darwin.Rendering.Editor.MoebiusColorRampValidation.RunBatch','-logFile',('"'+$testRoot+'/ColorRampValidation.log"'))
$process=Start-Process -FilePath $UnityPath -ArgumentList $colorRampUnityArguments -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(300000)){throw "Unity still running: $($process.Id)"}
if($process.ExitCode -ne 0){Get-Content "$testRoot/ColorRampValidation.log" -Tail 20;Get-Content "$testRoot/Validation/ColorRamp/$Label/Report.txt" -ErrorAction SilentlyContinue;throw 'Color ramp validation failed.'}
Get-Content "$testRoot/Validation/ColorRamp/$Label/Report.txt"
