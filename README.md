![GitHub Repo stars](https://img.shields.io/github/stars/dogederp/CasultiesUnknown_Player-sprite-replacer?style=plastic)
![GitHub forks](https://img.shields.io/github/forks/dogederp/CasultiesUnknown_Player-sprite-replacer?style=plastic)
![GitHub Release](https://img.shields.io/github/v/release/dogederp/CasultiesUnknown_Player-sprite-replacer?style=plastic)
![GitHub commits since latest release](https://img.shields.io/github/commits-since/dogederp/CasultiesUnknown_Player-sprite-replacer/latest?style=plastic)
![GitHub last commit](https://img.shields.io/github/last-commit/dogederp/CasultiesUnknown_Player-sprite-replacer?style=plastic)
![GitHub License](https://img.shields.io/github/license/dogederp/CasultiesUnknown_Player-sprite-replacer?style=plastic)


# Casualties Unknown - Player Sprite Replacer

A small hobby mod for Casualties Unknown that swaps player body/head sprites at runtime using BepInEx.

## Compatibility

- Loader: **BepInEx 5.4.23.5**
- Game version: **6.1 demo**

## About This Project

- This is a hobby project I made for fun.
- If the game creator **Orsoniks** does not want this mod to be public, please contact me and I will take action.

## Installation

### If BepInEx is already installed

1. Open this repository's `body-sprite-replacer/plugins` folder.
2. Drag/copy its contents into your game's BepInEx `plugins` folder.
3. Start the game.

### If BepInEx is not installed yet

- First install BepInEx by following the official instructions:
  - https://docs.bepinex.dev/articles/user_guide/installation/index.html
- After BepInEx is installed, follow the steps above.

## Usage

- While in-game, press **Numpad 1 through 9** to instantly switch between different character sprites.
- The mod will look for sprites in the `CustomSprites/st1` up to `CustomSprites/st9` folders.

## Sprite Credits

- st1 contains Mottrew by beebigirl https://skin.cat-bot.de/?skinid=163&page=4#skin-163
- (st2-st9) I copied the sprite files from GitHub user **Paili-16**. (I know I could have extracted them from the game files myself, but I wanted to save time.)

## Making Your Own Player Sprites

1. Back up your current `CustomSprites/st1/Body` and `CustomSprites/st1/Head` folders (or any other `st1`-`st9` folder) first.
2. Edit the images in those folders.
3. Keep the same dimensions/image sizes as the originals. Do not change the file names.

I have not tested what happens if this size restriction is not respected.

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
- Buy me a coffee: https://buymeacoffee.com/dogederp

## Star History

<a href="https://www.star-history.com/?repos=dogederp%2FCasultiesUnknown_Player-sprite-replacer&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=dogederp/CasultiesUnknown_Player-sprite-replacer&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=dogederp/CasultiesUnknown_Player-sprite-replacer&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=dogederp/CasultiesUnknown_Player-sprite-replacer&type=date&legend=top-left" />
 </picture>
</a>

## Looking for an alternative?

I found out about this project, give it a spin if my mod doesn't work out for you:

https://github.com/05126619z/ChangeSkin

