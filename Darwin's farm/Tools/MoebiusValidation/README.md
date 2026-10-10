# Optional Moebius CLI validation sources

Editor/*.cs are archived authoring/verification sources, intentionally outside Unity's Assets folder. Their original .cs.meta files are retained for recovery; the main Unity project does not import or compile this directory.

The existing commands in Tools/ use PrepareMoebiusValidation.ps1 to inject one validator into an isolated project's Assets/Editor/MoebiusValidation/. Invoke the command from the project root, for example:

```powershell
.\Tools\ValidateMoebiusPaint.ps1 -Label Sphere
.\Tools\ValidateHighlandPaint.ps1
```

No validation sources are required for playing the game, rendering existing materials, or painting with Tools > Moebius > Paint. The validators may depend on old scene fixtures and source snapshots; commands for removed fixtures cannot be rerun until those fixtures are restored.

PrepareMoebiusValidation.ps1 removes only known old validation copies in the verified temporary checkout before copying the selected source. It never removes files from the main project's Assets folder.
