# This file describes the expected file structure for the Body Sprite Replacer mod. The mod should be placed in the `BepInEx/plugins` directory, and custom sprites should be organized in subdirectories under `CustomSprites`, with each subdirectory named according to the character (e.g., `st1`, `st2`, etc.). Each character's directory should contain separate folders for body and head sprites.
```
BepInEx/
└── plugins/
    ├── body_sprite_replacer.dll
    └── CustomSprites/
        ├── st1/
        │   ├── Body/
        │   │   ├── some_body_sprite.png
        │   │   └── ...
        │   └── Head/
        │       ├── some_head_sprite.png
        │       └── ...
        ├── st2/
        │   ├── Body/
        │   └── Head/
        └── ... (up to st9)
```
