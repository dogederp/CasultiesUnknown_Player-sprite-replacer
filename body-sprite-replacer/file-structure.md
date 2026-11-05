# File structure

The mod goes in `BepInEx/plugins`. Custom sprites go in subfolders under `CustomSprites`. Each slot folder holds one skin set: `slot1` to `slot9`. Each folder can contain a `Body`, a `Head`, and an optional `WoundView` subfolder.

```
BepInEx/
└── plugins/
    ├── body_sprite_replacer.dll
    └── CustomSprites/
        ├── backups/
        │   └── slot1_20261031_120000/
        │       ├── Body/
        │       ├── Head/
        │       └── WoundView/
        ├── default/
        │   ├── Body/
        │   ├── Head/
        │   └── WoundView/
        ├── BaseSprites_woundview/
        │   └── BaseSprites/
        │       ├── Experiment_Limb_0_Middle.png
        │       └── ...
        ├── slot1/
        │   ├── Body/
        │   │   ├── some_body_sprite.png
        │   │   └── ...
        │   ├── Head/
        │   │   ├── some_head_sprite.png
        │   │   └── ...
        │   └── WoundView/
        │       ├── abdomenMiddle.png
        │       └── ...
        ├── slot2/
        │   ├── Body/
        │   ├── Head/
        │   └── WoundView/
        └── ... (up to slot9)
```

The **Switch** button writes a timestamped copy of the target slot to `backups/` before it replaces files. The mod creates `backups/` on demand. Each backup keeps the old `Body`, `Head`, and `WoundView` folders. The mod keeps the newest 5 backups for each slot by default. The **Backups kept per slot** setting changes the count. `0` keeps all backups.

Old `st1` to `st9` folders are renamed to `slot1` to `slot9` on the first launch. If both folders exist, the mod keeps both and uses the new name. Old backups with an `stN_` prefix still count toward the limit and are pruned.

The mod writes the game's own sprites to `default/` when it sees them. The folder uses the same `Body`, `Head`, and `WoundView` layout as a slot. The mod does not overwrite a file that is already there.

`BaseSprites_woundview/BaseSprites` is a reference folder. It holds 30 PNGs of the game's wound-view art. The mod does not load this folder. Its file names are not the slot file names. Use `default/WoundView` for the file names.
