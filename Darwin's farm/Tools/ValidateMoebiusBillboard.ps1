param([string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
$validationPath = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Temp/MoebiusBillboardValidation'))
if (-not $validationPath.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Validation must stay in this workspace.' }
New-Item -ItemType Directory -Force -Path "$validationPath/Assets/Rendering", "$validationPath/Assets/Scenes", "$validationPath/Packages", "$validationPath/ProjectSettings" | Out-Null
Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $validationPath
Copy-Item -LiteralPath "$projectRoot/Assets/Settings" -Destination "$validationPath/Assets" -Recurse -Force
Copy-Item -LiteralPath "$projectRoot/Assets/Scenes/Moebius Style Shader.unity", "$projectRoot/Assets/Scenes/Moebius Style Shader.unity.meta" -Destination "$validationPath/Assets/Scenes" -Force
Copy-Item -Path "$projectRoot/ProjectSettings/*" -Destination "$validationPath/ProjectSettings" -Force
Copy-Item -LiteralPath "$projectRoot/Packages/manifest.json", "$projectRoot/Packages/packages-lock.json" -Destination "$validationPath/Packages" -Force
Set-Content -LiteralPath "$validationPath/BillboardValidation.marker" -Value 'Isolated billboard test project'
$arguments = @('-batchmode','-projectPath',('"' + $validationPath + '"'),'-executeMethod','Darwin.Rendering.Editor.MoebiusBillboardPrototype.RunBatch','-logFile',('"' + $validationPath + '/Validation.log"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(300000)) { throw "Validation exceeded five minutes. Inspect $validationPath/Validation.log; process $($process.Id) is still running." }
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath "$validationPath/Validation.log" -Tail 50
    throw "Unity validation failed with exit code $($process.ExitCode)."
}
if (-not (Test-Path -LiteralPath "$validationPath/Validation/Report.txt")) { throw 'No validation report was produced.' }
Get-Content -LiteralPath "$validationPath/Validation/Report.txt"
Write-Output "Images and generated test assets: $validationPath"
