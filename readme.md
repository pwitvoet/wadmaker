# WadMaker
*"I certainly hope you know wad you're doing!"*

## Table of contents
- [Overview](#overview)
    - [Intended workflow](#intended-workflow)
    - [Requirements](#requirements)
- [How to use](#how-to-use)
    - [Basic usage](#basic-usage)
    - [Advanced options](#advanced-options)
    - [Texture-specific settings](#texture-specific-settings)
        - [Available settings](#available-settings)
        - [wadmaker.config format](#wadmaker.config-format)
- [About Half-Life textures](#about-half-life-textures)
- [Comparisons](#comparisons)
- [Custom converters](#custom-converters)
    - [Using IrfanView for color conversion](#using-irfanview-for-color-conversion)
- [Credits](#credits)

## Overview
WadMaker is a command-line tool that can turn directories full of images into Half-Life wad files. Existing wad files can be updated more quickly because only added, modified and removed images are processed by default. WadMaker can also extract textures from wad and bsp files, or remove embedded textures from bsp files.

WadMaker accepts image files (png, jpg, gif, bmp, tga), Photoshop files (psd, psb) and Krita files (kra, ora), and can be configured to call external conversion tools for other formats. It will automatically create a suitable 256-color palette for each image. It will also apply a limited form of dithering, which can be disabled if necessary. For transparent textures, the alpha channel of the input image is compared against a configurable threshold, but it is also possible to treat a specific input color as transparent. For water textures, the fog color and intensity are derived from the image itself, but they can also be specified explicitly. All these texture-specific settings can be overridden with a plain-text wadmaker.config file in the images directory.

### Intended workflow
Existing workflows sometimes involve a lot of steps, such as exporting or converting images to an 8-bit indexed format, manually adjusting palettes for special texture types, marking transparent areas with special colors, opening a GUI tool, dragging images into it, then saving the modified wad file, and so on.

WadMaker's aim is to simplify this. Dragging a directory onto WadMaker or running a single batch file should be enough to turn a directory full of images into a wad file. No exporting or converting, no palette adjustments, no clicking around in a GUI tool. Just modify some images, run a batch file, and go.

### Requirements
WadMaker requires .NET Framework 4.7.2 or higher. If you're using an up-to-date Windows 10 then you're ready to go. If you're using Windows 7 or 8 then you may need to [download .NET Framework from Microsoft's website](https://dotnet.microsoft.com/download/dotnet-framework).

## How to use
### Basic usage
For basic usage, directories and files can be dragged onto `WadMaker.exe`:
- To **make a wad file**, drag the folder that contains your images onto `WadMaker.exe`. A wad file with the same name as the directory will be created next to the directory. If the wad file already exists, then it will be updated, with only added, modified and removed images being processed.
- To **extract textures** from a wad or bsp file, drag the file onto `WadMaker.exe`. All textures will be saved to a 'filename_extracted' directory that will be created next to the wad or bsp file. This also works for bsp files with embedded textures. Existing images in this directory will not be overwritten by default.

### Advanced options
The behavior of WadMaker can be modified with several command-line options. To use these, you will have to call WadMaker from a command-line or from a batch file. The following options are available (options must be put before the input directory or file path):
- **-full** - Forces WadMaker to do a full wad rebuild, instead of updating an existing wad file.
- **-subdirs** - Makes WadMaker look for images in sub-directories.
- **-mipmaps** - Enables the extraction of texture mipmaps.
- **-overwrite** - Enables overwriting of existing files when extracting textures.
- **-remove** - Removes embedded textures from a bsp file.
- **-nologfile** - Stops WadMaker from creating a 'wadmaker - directoryname.log' file when making wad files.

It is also possible to specify a custom output location when making a wad file. For example:
`"C:\HL\tools\WadMaker.exe" -subdirs "C:\HL\mymod\textures\chapter1" "C:\Program Files (x86)\Steam\steamapps\common\Half-Life\mymod\chapter1.wad"`
will take all images in `C:\HL\mymod\textures\chapter1` and its sub-directories, and use them to create or update `C:\Program Files (x86)\Steam\steamapps\common\Half-Life\mymod\chapter1.wad`.

Likewise, it's possible to save extracted textures to a specific location. For example:
`"C:\HL\tools\WadMaker.exe" -mipmaps -overwrite "C:\Program Files (x86)\Steam\steamapps\common\Half-Life\valve\halflife.wad" "C:\HL\extracted\halflife"`
will extract all textures, including mipmaps, from `C:\Program Files (x86)\Steam\steamapps\common\Half-Life\valve\halflife.wad`, and save them to `C:\HL\extracted\halflife`, overwriting any existing files in that directory.

### Texture-specific settings
Various settings can be specified per texture, or per group of textures, by creating a plain-text `wadmaker.config` file in the images directory.

#### Available settings
General settings:
- **dithering: type** - Type must be either `none` or `floyd-steinberg`. By default, Floyd-Steinberg dithering is applied.
- **dither-scale: scale** - Scale must be a value between 0 (disables dithering) and 1 (full error diffusion). The default is 0.75, which softens the effect somewhat.
- **converter: 'path'** - The path of an application that can convert a file into a png file. If the path contains spaces then it should be surrounded by double quotes. The whole path, including any double quotes, must be delimited by single quotes. Any single quotes in the path itself must be escaped with a `\`. For example, the path `C:\what's that.exe` should be written as `'"C:\what\'s that.exe"'`.
- **arguments: 'arguments'** - The arguments that will be passed to the converter application, surrounded by single quotes. This must contain the placeholders `{input}` and `{output}`. WadMaker will replace `{input}` with the path of the current file, and `{output}` with a path where the converter tool must save its output. This output file is then used to create a texture. As with the `converter` setting, the whole arguments list must be delimited by single quotes, and any path that contains spaces (including `{input}` and `{output}`) should be surrounded by double quotes.

Transparent textures (textures whose name starts with a `{`):
- **transparency-threshold: threshold** - Threshold must be a value between 0 and 255. The default is 128. Any pixel whose alpha value is below this threshold will be marked as transparent.
- **transparency-color: red green blue** - A color, written as 3 whitespace-separated numbers, with each number between 0 and 255. Pixels with this color will be marked as transparent.

Water textures (textures whose name starts with a `!`):
- **water-fog: red green blue intensity** - The water fog color and intensity, written as 4 whitespace-separated numbers, with each number between 0 and 255. By default, the fog color and intensity are derived from the average color of the image.

Decal textures (these settings are only applied when the output wad name is `decals.wad`):
- **decal-transparency: type** - Type must be either `alpha` or `grayscale`. By default, alpha is used: pixels with higher alpha values will be more visible in game. When grayscale is used, pixels that are whiter will be more visible in game.
- **decal-color: red green blue** - The decal color, written as 3 whitespace-separated numbers, with each number between 0 and 255. By default, the average color of the image is used.

#### wadmaker.config format
A settings line starts with a texture name or a name pattern, followed by one or more settings. Empty lines and comments are ignored. For example:

    // This is a comment. The next 4 lines contain texture settings:
    *            dither-scale: 0.5
    bluewater    water-fog: 0 0 255 127
    {lab*        dithering: none     transparency-threshold: 200
    *.pdn        converter: '"C:\Tools\PdnToPngConverter.exe"'       arguments: '/in="{input}" /out="{output}"'
This sets the dither-scale to 0.5 for all textures, and explicitly defines the water fog color and intensity for a texture named 'bluewater'. It also disables dithering and sets a custom transparency threshold for all textures whose name starts with '{lab'. Finally, it tells WadMaker to call a converter application for each .pdn file in the image directory - WadMaker will then use the output image produced by that application.

If there are multiple matching rules, then all of their settings will be applied, but more specific rules will override settings defined by less specific rules. In the above example, a texture named `bluewater` will use a dither-scale of 0.5 (because of the `*` rule) and a water-fog color/intensity of 0,0,255,127 (because of the `bluewater` rule). If the `bluewater` rule would also have specified a dither-scale, then that dither-scale would have been used instead, because a texture name rule is more specific than a wildcard rule.

WadMaker keeps track of settings history in a `wadmaker.dat` file. This enables it to only update textures whose settings have been modified (if `-full` mode is not enabled).

### About Half-Life textures
Half-Life textures use a 256-color palette, and their width and height must be multiples of 16. Texture names cannot be longer than 15 characters and cannot contain spaces. Texture name matching is case-insensitive ('aa' and 'AA' are seen as the same texture name).

Note that wad files do not store color profile information. Some testing has shown that Half-Life (and Wally) does not appear to apply gamma correction properly on all systems. This means that on some systems, textures (especially dark ones) will look too bright.

The game supports several special texture types. The type of a texture depends on the first part of its name:
- `{` is for textures with transparent areas. The last color in the palette (index 255) is used for transparent pixels.
- `!` is for water textures. The 4th palette color (index 3) is used as water fog color, and the red channel of the 5th color (index 4) is used as fog intensity (a higher intensity results in a lower view distance).
- `scroll` (lowercase, uppercase won't work) is for scrolling textures. These are used in conjunction with the `func_conveyor` entity.
- `+0`-`+9` (and `+A`-`+J`, or `+a`-`+j`) are for animated textures. The game will automatically cycle to the next texture in the sequence every 0.1 second. If the texture is applied to an entity that can be toggled, then the game will switch between the numbered and the 'lettered' sequence whenever the entity is toggled.

There is also a special wad file, `decals.wad`, which contains decal textures. These textures behave similar to index-alpha sprites: their image data is used as an alpha channel, and the last color in their palette (index 255) determines the overall color of the decal. The rest of the palette is ignored. Decal texture names don't need to start with a `{`, but doing so will make them show up correctly in certain editors (J.A.C.K.).

Additionally, some textures serve a special purpose for the map compile tools, such as:
- `SKY` is used to mark brushes as a 'sky brushes'. Such brushes won't be visible in-game, but the skybox will be shown instead.
- `ORIGIN` is used to create 'origin brushes', whose center is used as the origin of brush-based entities.
- `CLIP` is used to block player movement.
- `NULL` is used to remove surfaces that are not visible to the player.
- `HINT` (along with `SKIP`) is used to force a bsp node cut. Strategic use of this can improve performance.

## Comparisons
Just like Wally, WadMaker can convert true-color images to the 256-color indexed format that Half-Life uses. For textures that do not contain a wide range of colors and gradients, this often does not lead to a perceptible loss of quality. In cases where it does matter, WadMaker tends to produce better results than Wally due to its use of dithering, but it does worse than IrfanView:

![input, WadMaker, IrfanView, Wally](/documentation/images/comparison.png "input, WadMaker, IrfanView, Wally")
*(free texture downloaded from https://textures.pixel-furnace.com, scaled-down to 256x256 pixels)*

## Custom converters
WadMaker can be configured to use custom converters for certain textures. This makes it possible to achieve better visual results, or to handle file types that WadMaker does not support directly. IrfanView is particularly useful in this regard, but any other command-line program can be used, as long as both the input and output path can be provided as arguments.

### Using IrfanView for color conversion
To use IrfanView to convert images to 256 colors, add the following line to your `wadmaker.config` file:

    texturename     converter: '"C:\Program Files\IrfanView\i_view64.exe"' arguments: '"{input}" /silent /bpp=8 /convert="{output}"'
Or, when using advanced batch settings, save the right IrfanView batch settings to an `i_view64.ini` file, and specify the directory in which that ini file is located:

    texturename     converter: '"C:\Program Files\IrfanView\i_view64.exe"' arguments: '"{input}" /silent /ini="C:\custom_irfanview_settings_dir" /advancedbatch /convert="{output}"'
To use this conversion for multiple images, replace `texturename` with a wildcard pattern such as `if_*`, so any texture whose name starts with `if_` will be converted using IrfanView.

## Credits
- Thanks to [Yuraj](https://yuraj.ucoz.com) for his unofficial wad3 file format specification.
- Thanks to [The303](http://www.the303.org/) for his information about special texture types.
- WadMaker uses the [ImageSharp](https://github.com/SixLabors/ImageSharp) library, which is licensed under the Apache License 2.0.