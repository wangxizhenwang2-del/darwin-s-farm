# Ye-Branch UI integration

The accepted Journal UI, full-world save system, and seven-chapter handbook are in
`Assets/Scenes/WhiteBox.unity` on `Ye-Branch`.

## Scene and content

- `JournalCanvas` contains the title, pause, settings, information, save slots,
  confirmation dialog, ecology news HUD, and tutorial controls. Visual objects
  are serialized in the scene rather than created at runtime.
- The tutorial asset has seven chapters and 26 pages. Five colored-pencil
  illustrations are reused where they help explain the text. The journal
  adjusts its paper layout for portrait and landscape safe areas.
- Three save slots live under `Application.persistentDataPath/Saves`. Each
  slot stores a versioned, checksummed world snapshot and has a backup copy.

## Integration notes

- The main worktree already contained the initial Journal UI. This integration
  replaced only its `JournalCanvas` scene subtree: 171 existing scene documents
  changed and 139 were added. All 2,015 other scene documents were preserved.
- The save hooks in simulation and ecology files were copied without the
  unrelated mutation implementation from the acceptance worktree.
- Unity-generated `.csproj` files were refreshed locally for a compile check;
  they are ignored and not part of the commits.
- If WhiteBox was open during the external scene edit, reopen it from disk
  before saving the scene in Unity.

## Verification

- `dotnet build Assembly-CSharp-Editor.csproj --no-restore -v:q -clp:ErrorsOnly`
  in the main worktree: zero errors.
- The acceptance worktree previously passed `JournalSceneValidator.ValidateBatch`
  and `JournalSaveSmoke.RunBatch`. The main-worktree transfer received a
  compile and serialized scene merge check; no separate device run was done.
