# Casualties Unknown - Player Sprite Replacer

A small hobby mod for Casualties Unknown that swaps player body/head sprites at runtime using BepInEx.

## Compatibility

- Loader: **BepInEx 5.4.23.5**
- Game version: **5.1 demo**

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

## Sprite Credits

- I copied the sprite files from GitHub user **Paili-16**. (I know I could have extracted them from the game files myself, but I wanted to save time.)

## Making Your Own Player Sprites

1. Back up your current `CustomSprites/Body` and `CustomSprites/Head` folders first.
2. Edit the images in those folders.
3. Keep the same dimensions/image sizes as the originals. Do not change the file names.

I have not tested what happens if this size restriction is not respected.

## Finding sprites

Links that have more custom sprites that I currently know of:

https://skin.cat-bot.de/ (make sure to spit the Body and Head assets into separate files until I change the code to support one file)

https://github.com/Paili-16/Scav-Prototype-The-Characters-Skins


## Notes

- Editing `sharedassets1.assets` is **not** needed for this mod.

## Contributing / Support

- I am open to pull requests to improve the code.
- Feel free to open an issue for questions or concerns.

## Looking for an alternative?

I found out about this project, give it a spin if my mod doesn't work out for you:

https://github.com/05126619z/ChangeSkin

