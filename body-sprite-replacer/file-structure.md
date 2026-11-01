# This file describes the expected file structure for the Body Sprite Replacer mod. The mod should be placed in the `BepInEx/plugins` directory, and custom sprites should be organized in subdirectories under `CustomSprites`, with each subdirectory named according to the character (e.g., `st1`, `st2`, etc.). Each character's directory should contain separate folders for body and head sprites, plus an optional folder for wound view sprites.
```
BepInEx/
└── plugins/
    ├── body_sprite_replacer.dll
    └── CustomSprites/
        ├── backups/
        │   └── st1_20261031_120000/
        │       ├── Body/
        │       ├── Head/
        │       └── WoundView/
        ├── st1/
        │   ├── Body/
        │   │   ├── some_body_sprite.png
        │   │   └── ...
        │   ├── Head/
        │   │   ├── some_head_sprite.png
        │   │   └── ...
        │   └── WoundView/
        │       ├── abdomenMiddle.png
        │       └── ...
        ├── st2/
        │   ├── Body/
        │   ├── Head/
        │   └── WoundView/
        └── ... (up to st9)
```

The **Switch** button writes a timestamped copy of the target slot to `backups/` before it replaces files. The mod creates `backups/` on demand. Each backup keeps the old `Body`, `Head`, and `WoundView` folders.
