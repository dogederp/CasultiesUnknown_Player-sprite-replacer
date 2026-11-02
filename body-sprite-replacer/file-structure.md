# File structure

The mod goes in `BepInEx/plugins`. Custom sprites go in subfolders under `CustomSprites`. Each folder holds one skin set: `slot1` to `slot9`. Each folder can contain a `Body`, a `Head`, and an optional `WoundView` subfolder.

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
