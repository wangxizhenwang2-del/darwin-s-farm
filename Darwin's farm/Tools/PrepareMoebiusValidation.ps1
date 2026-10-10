# Refresh the source fixture so moved assets do not leave duplicate GUIDs at old paths.
function Copy-MoebiusRenderingSource {
    param([Parameter(Mandatory)][string]$ProjectRoot,[Parameter(Mandatory)][string]$ValidationPath)
    $workspacePath=[IO.Path]::GetFullPath($ProjectRoot)
    $temporaryRoot=[IO.Path]::GetFullPath((Join-Path $workspacePath 'Temp'))+[IO.Path]::DirectorySeparatorChar
    $targetRoot=[IO.Path]::GetFullPath($ValidationPath)
    if(-not $targetRoot.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Rendering fixtures may only be refreshed inside this project Temp folder.'}
    $source=Join-Path $workspacePath 'Assets/Rendering/Moebius'
    if(-not (Test-Path -LiteralPath $source -PathType Container)){throw 'Rendering source folder is unavailable.'}
    $parent=Join-Path $targetRoot 'Assets/Rendering'
    $destination=[IO.Path]::GetFullPath((Join-Path $parent 'Moebius'))
    if(-not $destination.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe rendering fixture destination.'}
    if(Test-Path -LiteralPath $destination){Remove-Item -LiteralPath $destination -Recurse -Force}
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $parent -Recurse -Force
    Copy-Item -LiteralPath ($source+'.meta') -Destination ($destination+'.meta') -Force
}

# Only inject the requested validator into the temporary Unity project.
function Install-MoebiusValidationSource {
    param([Parameter(Mandatory)][string]$ProjectRoot,[Parameter(Mandatory)][string]$ValidationPath,[Parameter(Mandatory)][string]$SourceFile)
    $workspacePath=[IO.Path]::GetFullPath($ProjectRoot)
    $temporaryRoot=[IO.Path]::GetFullPath((Join-Path $workspacePath 'Temp'))+[IO.Path]::DirectorySeparatorChar
    $targetRoot=[IO.Path]::GetFullPath($ValidationPath)
    if(-not $targetRoot.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Validation sources may only be installed inside this project Temp folder.'}
    if([IO.Path]::GetFileName($SourceFile) -ne $SourceFile){throw 'Pass a validation source filename, not a path.'}
    $source=Join-Path $workspacePath ('Tools/MoebiusValidation/Editor/'+$SourceFile)
    if(-not (Test-Path -LiteralPath $source)){throw "Validation source is unavailable: $SourceFile"}
    # Old isolated projects may contain former in-Assets copies. Remove only known files
    # in this verified temporary checkout so they cannot define duplicate C# classes.
    $archive=Join-Path $workspacePath 'Tools/MoebiusValidation/Editor'
    foreach($file in Get-ChildItem -LiteralPath $archive -Filter '*.cs'){
        foreach($oldFolder in @('Assets/Rendering/Moebius/BiomeTests/Editor','Assets/Rendering/Moebius/Samples/Biomes/Editor','Assets/Rendering/Moebius/Editor/Paint','Assets/Editor/MoebiusValidation')){
            $oldFile=Join-Path $targetRoot ($oldFolder+'/'+$file.Name)
            foreach($oldPath in @($oldFile,($oldFile+'.meta'))){if(Test-Path -LiteralPath $oldPath){Remove-Item -LiteralPath $oldPath}}
        }
    }
    foreach($drawerFolder in @('Assets/Rendering/Moebius/BiomeTests/Editor','Assets/Rendering/Moebius/Samples/Biomes/Editor')){
        $oldDrawer=Join-Path $targetRoot ($drawerFolder+'/MoebiusRampThresholdDrawer.cs')
        foreach($oldPath in @($oldDrawer,($oldDrawer+'.meta'))){if(Test-Path -LiteralPath $oldPath){Remove-Item -LiteralPath $oldPath}}
    }
    # Reload commands reuse an existing fixture rather than recopying all assets.
    # Ensure that an older fixture also receives the retained Inspector drawer.
    $inspectorSource=Join-Path $workspacePath 'Assets/Rendering/Moebius/Editor/Inspector'
    $inspectorParent=Join-Path $targetRoot 'Assets/Rendering/Moebius/Editor'
    New-Item -ItemType Directory -Path $inspectorParent -Force | Out-Null
    Copy-Item -LiteralPath $inspectorSource -Destination $inspectorParent -Recurse -Force
    $destination=Join-Path $targetRoot 'Assets/Editor/MoebiusValidation'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $source,($source+'.meta') -Destination $destination -Force
}

# Apply archived flat shader sources to the current modular layout in an isolated fixture.
function Install-MoebiusShaderBaseline {
    param([Parameter(Mandatory)][string]$ProjectRoot,[Parameter(Mandatory)][string]$ValidationPath,[Parameter(Mandatory)][string]$SourceFolder)
    $workspacePath=[IO.Path]::GetFullPath($ProjectRoot)
    $temporaryRoot=[IO.Path]::GetFullPath((Join-Path $workspacePath 'Temp'))+[IO.Path]::DirectorySeparatorChar
    $targetRoot=[IO.Path]::GetFullPath($ValidationPath)
    if(-not $targetRoot.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Shader baselines may only be installed inside this project Temp folder.'}
    $shaderRoot=Join-Path $targetRoot 'Assets/Rendering/Moebius/Shaders'
    $targets=@(Get-ChildItem -LiteralPath $shaderRoot -Recurse -File | Where-Object {$_.Extension -in @('.shader','.hlsl')})
    $encoding=[Text.UTF8Encoding]::new($false)
    foreach($source in Get-ChildItem -LiteralPath $SourceFolder -File | Where-Object {$_.Extension -in @('.shader','.hlsl')}){
        $matches=@($targets | Where-Object {$_.Name -eq $source.Name})
        if($matches.Count -ne 1){throw "Expected exactly one shader destination for $($source.Name), found $($matches.Count). Use a clean validation fixture."}
        $text=[IO.File]::ReadAllText($source.FullName)
        foreach($include in $targets | Where-Object {$_.Extension -eq '.hlsl'}){
            $relative=$include.FullName.Substring($targetRoot.Length+1).Replace('\','/')
            $text=$text.Replace('#include "'+$include.Name+'"','#include "'+$relative+'"')
        }
        [IO.File]::WriteAllText($matches[0].FullName,$text,$encoding)
    }
}
