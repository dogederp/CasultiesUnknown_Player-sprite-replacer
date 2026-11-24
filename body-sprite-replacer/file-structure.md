# File structure

The mod goes in `BepInEx/plugins`. Custom sprites go in `CustomSprites`. The folder tree has one branch for each species: `Exp`, `Milky`, and `Dune`. Each branch holds a `default` folder and `slot1` to `slot9`.

The menu has three species pages: **Expie**, **Milky**, and **Dune**. A page reads and writes only its own branch. Each page remembers its own slot.

```
BepInEx/
└── plugins/
    ├── body_sprite_replacer.dll
    └── CustomSprites/
        ├── Exp/
        │   ├── default/
        │   │   ├── Body/
        │   │   ├── Head/
        │   │   └── WoundView/
        │   ├── slot1/
        │   │   ├── Body/
        │   │   ├── Head/
        │   │   └── WoundView/
        │   ├── slot2/
        │   ├── ... (up to slot9)
        │   └── backups/
        │       └── slot1_20261118_120000/
        │           ├── Body/
        │           ├── Head/
        │           └── WoundView/
        ├── Milky/
        │   ├── default/
        │   ├── slot1/
        │   ├── ... (up to slot9)
        │   └── backups/
        ├── Dune/
        │   ├── default/
        │   ├── slot1/
        │   ├── ... (up to slot9)
        │   └── backups/
        ├── backups/                 (legacy root location)
        ├── BaseSprites_woundview/
        │   └── BaseSprites/
        │       ├── Experiment_Limb_0_Middle.png
        │       └── ...
        └── mp_cache/
```

## Species branches

- `Exp` is the Experiment species. The menu page label is **Expie**.
- `Milky` is the Milky species.
- `Dune` is the Dune species.

Sprite names in the game use the live species prefix: `experiment*`, `milky*`, or `dune*`. A skin on the Milky or Dune page can also use `experiment*` names. The mod maps those names to the live species names when it loads the folder.

## The default folder

`default` is slot 0. It holds the game's own sprites for that species. **Numpad 0** selects it on the active page. The mod never shares or uploads the `default` folder in multiplayer.

The mod writes new stock sprites to `<species>/default` when it sees them. An existing PNG is never overwritten. Delete one file to get a fresh copy.

## Old folder names

Before the species layout, the folders were `CustomSprites/slot1` to `CustomSprites/slot9` and `CustomSprites/default`. On the first launch, the mod moves these folders into `CustomSprites/Exp`. A folder is not moved when the target already exists. Old `st1` to `st9` names are renamed to `slot1` to `slot9` inside each species branch.

`CustomSprites/backups` stays in its old root location. New backups go to `<species>/backups`.

`CustomSprites/mp_cache` is shared by all species.

## Backups

**Switch** writes a timestamped copy of the target slot to `<species>/backups` before it replaces the slot. The folder name starts with the slot name, for example `slot1_20261118_120000`.

The mod keeps the newest 5 backups for each slot and species. The **Backups kept per slot** setting changes the count. `0` keeps all backups.

## Sprite folders

Each slot can hold a `Body`, a `Head`, and an optional `WoundView` folder.

- `Body` holds the body and limb sprites.
- `Head` holds the head, eye, and face sprites.
- `WoundView` holds the art for the health screen.

### Side sprite files

A `Body` or `Head` PNG can have a `_left` or a `_right` suffix. The suffix selects the facing direction:

```text
Body/
  experimentUpTorso.png
  experimentUpTorso_left.png
  experimentUpTorso_right.png
```

A missing side file falls back to the file without a suffix. A skin with no side files works as before. `WoundView` does not use the suffix. The import copies the suffix and keeps the shared name as the sprite name.

`CustomSprites/BaseSprites_woundview/BaseSprites` is a reference folder. The mod does not load it.
