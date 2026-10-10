param(
    [ValidateRange(1,6)][int]$Stage = 6,
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/6000.2.9f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'PrepareMoebiusValidation.ps1')
$validationPath = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Temp/MoebiusValidation'))
if (-not $validationPath.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Validation path must be inside this workspace.'
}
if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity editor not found: $UnityPath" }
New-Item -ItemType Directory -Force -Path "$validationPath/Assets/Rendering/Moebius", "$validationPath/Packages", "$validationPath/ProjectSettings" | Out-Null
Copy-MoebiusRenderingSource -ProjectRoot $projectRoot -ValidationPath $validationPath
Copy-Item -LiteralPath "$projectRoot/ProjectSettings/ProjectVersion.txt" -Destination "$validationPath/ProjectSettings/ProjectVersion.txt" -Force
Set-Content -LiteralPath "$validationPath/Packages/manifest.json" -Value '{"dependencies":{"com.unity.render-pipelines.universal":"17.2.0"}}'
Set-Content -LiteralPath "$validationPath/ValidationStage.txt" -Value $Stage
if (Test-Path -LiteralPath "$validationPath/Validation/Report.txt") {
    Remove-Item -LiteralPath "$validationPath/Validation/Report.txt"
}
$arguments = @('-batchmode','-projectPath',('"' + $validationPath + '"'),'-executeMethod','Darwin.Rendering.Editor.MoebiusValidation.RunBatch','-logFile',('"' + $validationPath + '/Validation.log"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(300000)) {
    throw "Unity validation exceeded 5 minutes. Process $($process.Id) is still running; inspect $validationPath/Validation.log."
}
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath "$validationPath/Validation.log" -Tail 40
    throw "Unity validation failed with exit code $($process.ExitCode)."
}
if (-not (Test-Path -LiteralPath "$validationPath/Validation/Report.txt")) { throw 'Unity did not produce a validation report.' }
Get-Content -LiteralPath "$validationPath/Validation/Report.txt"
Write-Output "Validation images: $validationPath/Validation"
