![GitHub Repo stars](https://img.shields.io/github/stars/dogederp/CasultiesUnknown_Player-sprite-replacer?style=plastic)
![GitHub forks](https://img.shields.io/github/forks/dogederp/CasultiesUnknown_Player-sprite-replacer?style=plastic)
![GitHub Release](https://img.shields.io/github/v/release/dogederp/CasultiesUnknown_Player-sprite-replacer?style=plastic)
![GitHub commits since latest release](https://img.shields.io/github/commits-since/dogederp/CasultiesUnknown_Player-sprite-replacer/latest?style=plastic)
![GitHub License](https://img.shields.io/github/license/dogederp/CasultiesUnknown_Player-sprite-replacer?style=plastic)


# Casualties Unknown - Player Sprite Replacer

A small hobby mod for Casualties Unknown that swaps player body/head sprites at runtime using BepInEx.

## Compatibility

- Loader: **BepInEx 5.4.23.5**
- Game version: **Steam demo, April 21, 2026 build**
- Optional: **CUCoreLib 1.0.6.1 or newer** for the in-game settings tab. The skin selector works without it.

## About This Project

- This is a hobby project I made for fun.
- If the game creator **Orsoniks** does not want this mod to be public, please contact me and I will take action.

## Installation

### If BepInEx is already installed

1. Open this repository's `body-sprite-replacer/plugins` folder.
2. Drag/copy its contents into your game's BepInEx `plugins` folder.
3. Optional: copy `CUCoreLib.dll` into the same folder to get the Skin Replacer settings tab.
4. Start the game.

### If BepInEx is not installed yet

- First install BepInEx by following the official instructions:
  - https://docs.bepinex.dev/articles/user_guide/installation/index.html
- After BepInEx is installed, follow the steps above.

## Usage

- Click the **top-left button** in the main menu or the Escape menu.
- The skin selector shows all nine slots: `st1` through `st9`.
- **Select** uses an existing slot. **Switch** imports a whole skin folder into that slot.
- **Numpad 1 through 9** changes the active slot while you play.
- The mod remembers the last slot you selected and loads it at the next launch.
- The mod looks for sprites in `CustomSprites/st1` up to `CustomSprites/st9`.
- The button is hidden in the wound view, crafting panel, trade menu, and settings menu.
- With CUCoreLib installed, the game's settings menu has a **Skin Replacer** tab with an active-slot dropdown.

### Switch and backups

- The **Switch** button asks for a folder that contains `Body` and `Head`.
- The mod checks the PNG files and the sprite names before it copies anything.
- An image size that differs from the current slot gives a warning, but the import continues.
- It writes a timestamped backup of the target slot to `CustomSprites/backups/`.
- It replaces only the supplied subfolders and removes old `.txt` files from them.

The mod does not create a pause and does not change the time scale.

## Sprite Credits

- st1 contains Mottrew by beebigirl https://skin.cat-bot.de/?skinid=163&page=4#skin-163
- (st2-st9) I copied the sprite files from GitHub user **Paili-16**. (I know I could have extracted them from the game files myself, but I wanted to save time.)

## Making Your Own Player Sprites

1. Back up your current `CustomSprites/st1/Body` and `CustomSprites/st1/Head` folders (or any other `st1`-`st9` folder) first.
2. Edit the images in those folders.
3. Keep the same dimensions/image sizes as the originals. Do not change the file names.

A different image size is allowed. The mod shows a warning, and the sprite can look larger or smaller in the game.

You can also use **Switch** to import a prepared skin folder. The folder must contain a `Body` and/or `Head` subfolder. The PNG file names must match the game sprites exactly. The importer warns about missing names.

## Finding sprites

Links that have more custom sprites that I currently know of:

https://skin.cat-bot.de/ (make sure to spit the Body and Head assets into separate files until I change the code to support one file)

https://github.com/Paili-16/Scav-Prototype-The-Characters-Skins


## Notes

- Editing `sharedassets1.assets` is **not** needed for this mod.

## Credits

- Main idea of switching sprites using numpad thanks to rusiber1231 ([#1](https://github.com/dogederp/CasultiesUnknown_Player-sprite-replacer/issues/1))

## Contributing / Support

- I am open to pull requests to improve the code.
- Feel free to open an issue for questions or concerns.
- Please consider giving this repository a star if you find it useful!

## Looking for an alternative?

I found out about this project, give it a spin if my mod doesn't work out for you:

https://github.com/05126619z/ChangeSkin

