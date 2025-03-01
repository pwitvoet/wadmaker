# SpriteMaker
*"Shouldn't you be guarding some coffee and donuts sprite about now?"*

## Table of contents
- [Overview](#overview)
    - [Intended workflow](#intended-workflow)
- [How to use](#how-to-use)
    - [Basic usage](#basic-usage)
    - [Advanced options](#advanced-options)
    - [Sprite-specific settings](#sprite-specific-settings)
        - [Filename settings](#filename-settings)
        - [spritemaker.config files](#spritemakerconfig-files)
        - [spritemaker.config settings](#spritemakerconfig-settings)
- [About Half-Life sprites](#about-half-life-sprites)
    - [Sprite orientations](#sprite-orientations)
    - [Sprite texture formats](#sprite-texture-formats)
- [Custom converters](#custom-converters)
    - [Using IrfanView for color conversion](#using-irfanview-for-color-conversion)
    - [Converting Gimp files](#converting-gimp-files)
    - [Converting Aseprite files](#converting-aseprite-files)
- [Credits](#credits)

## Overview
SpriteMaker is a command-line tool that converts directories full of images to Half-Life sprites. Existing sprite directories can be updated quickly because only added, modified and removed images are processed. SpriteMaker can also convert sprites back to images.

SpriteMaker accepts image files (png, jpg, gif, bmp, tga), Photoshop files (psd, psb) and Krita files (kra, ora), and can be configured to call external conversion tools for other formats. It will automatically create a suitable 256-color palette for each sprite. By default it also applies a limited form of dithering to single-frame sprites. For index-alpha and alpha-test sprites, SpriteMaker expects input images with transparency, but it can also be configured to accept grayscale images or images where transparent parts are marked with a special color. All these settings can be specified in a plain-text spritemaker.config file in the images directory. The most common settings, such as sprite orientation and texture format, can also be set with input image filenames.

### Intended workflow
Existing workflows sometimes involve a lot of steps, such as exporting or converting images to an 8-bit indexed format, manually creating a palette and applying it to multiple frames, marking transparent areas with special colors, opening a GUI tool, dragging images into it, then saving the modified sprites, and so on.

SpriteMaker aims to simplify this. Dragging a directory onto SpriteMaker or running a single batch file should be enough to convert all images in a directory to sprites. No exporting or converting, no palette adjustments, no clicking around in a GUI tool. Just modify some images, run a batch file, and go.

## How to use
### Basic usage
For basic usage, directories and files can be dragged onto `SpriteMaker.exe`:
- To **make multiple sprites**, drag the directory that contains your images onto `SpriteMaker.exe`. Sprites will be put in a 'directoryname_sprites' folder next to the input folder. If the output folder already exists, then only added, modified and removed images will be processed.
- To **make a single sprite**, drag an image onto `SpriteMaker.exe`. A 'filename.spr' file will be created next to the input file.
- To **convert a sprite back to an image**, drag a sprite onto `SpriteMaker.exe`. One or more images will be created next to the input file. To convert a directory full of sprites, see the `-extract` option below.

### Advanced options
The behavior of SpriteMaker can be modified with several command-line options. To use these, you will have to call SpriteMaker from a command-line or from a batch file. The following options are available (options must be put before the input directory or file path):
- **-subdirs** - Makes SpriteMaker also process sub-directories, creating a matching output folder hierarchy.
- **-full** - Forces SpriteMaker to rebuild all sprites, instead of processing only added, modified and deleted images.
- **-subdirremoval** - Enables deleting of output sub-directories, when input sub-directories are removed.
- **-extract** - Switches to extraction mode. This enables the extraction of directories full of sprites.
- **-spritesheet** - Animated sprites will be extracted as spritesheet images, instead of a sequence of images.
- **-overwrite** - Enables overwriting of existing image files when extracting sprites to images.
- **-format: \<fmt\>** - Extracted images output format (\<fmt\> must be `png`, `jpg`, `gif`, `bmp` or `tga`). If the output format is `gif`, animated sprites will be extracted to animated gif files.
- **-indexed** - Extract textures as 8-bit indexed images (only works with png and bmp).
- **-nologfile** - Prevents SpriteMaker from creating log files.

It is also possible to specify a custom output location when making sprites. For example:
`"C:\HL\tools\SpriteMaker.exe" -subdirs -subdirremoval "C:\HL\mymod\sprites" "C:\Program Files (x86)\Steam\steamapps\common\Half-Life\mymod\sprites"` will take all images in `C:\HL\mymod\sprites` and its sub-directories, and use them to create, update or remove sprites in `C:\Program Files (x86)\Steam\steamapps\common\Half-Life\mymod\sprites`.

The same can be done when converting sprites back to images. For example:
`"C:\HL\tools\SpriteMaker.exe" -extract -subdirs -overwrite "C:\Program Files (x86)\Steam\steamapps\common\Half-Life\mymod\sprites" "C:\HL\extracted\sprites"` will convert all sprites in `C:\Program Files (x86)\Steam\steamapps\common\Half-Life\mymod\sprites` and its sub-directories, and store the resulting images in `C:\HL\extracted\sprites`, overwriting any existing files in that directory and its sub-directories.

### Sprite-specific settings
#### Filename settings

Some settings can be specified in the filename of an image. These take precedence over settings from `spritemaker.config` files. Filename settings are separated by dots, the part before the first dot becomes the output sprite filename. For example, `fire.oriented.index-alpha.png` (or `fire.o.ia.png`) produces a `fire.spr` sprite with a fixed orientation and index-alpha texture format.

**Sprite orientation:**

- `.parallel-upright` or `.pu` - Makes the sprite always face the camera, but locks it along the z-axis.
- `.upright` or `.u` - Similar to 'parallel-upright', but faces the player's origin instead of the camera.
- `.parallel` or `.p` - Makes the sprite always face the camera. *This is the default orientation so it does not need to be specified explicitly.*
- `.oriented` or `.o` - Creates a sprite with a fixed orientation that can be set in the level editor.
- `.parallel-oriented` or `.po` - Similar to 'oriented', but the sprite will also face the camera.

**Texture format:**

- `.normal` or `.n` - Creates a 256-color sprite, with no support for transparency. This behaves the same as the 'additive' format.
- `.additive` or `.a` - Creates a 256-color sprite, where the brightness of each pixel determines its transparency (black being fully transparent, white being fully opaque). This does require a sprite entity to use the 'additive' render mode. *This is the default texture format so it does not need to be specified explicitly.*
- `.index-alpha` or `.ia` - Creates a 1-color sprite, with 256 levels of transparency, similar to how decal textures work.
- `.alpha-test` or `.at` - Creates a 255-color sprite, with support for transparency. Pixels are either fully opaque or fully transparent, similar to how transparent textures work.

**Spritesheet size:**

- `.{width}x{height}` - Spritesheet images are cut up into multiple tiles, with each tile producing a separate frame. Tiles are read from left to right, then from top to bottom. The spritesheet image size should be a multiple of the tile size. For example, an `explosion.32x32.png` image that's 128 x 64 pixels will result in a 32x32 sprite with 8 frames.

**Frame number:**

- `.{number}` - Animated sprites can also be created from a sequence of numbered images. The image with the lowest number is used for the first frame, the image with the next number is used for the second frame, and so on. This can also be combined with spritesheets and multi-frame gif files. Sprite orientation and texture format settings must be specified in the filename of the first image.

**Frame offset:**

- `.@{x},{y}` - The offset of a frame, relative to the sprite's center. Positive x values move the frame towards the right, positive y values move it upwards. The default is 0, 0, which centers the frame at the sprite's center.

#### spritemaker.config files

Less common settings can be specified per sprite, or per group of sprites, by creating a plain-text `spritemaker.config` file in the images directory. For global settings, use the `spritemaker.config` file in SpriteMaker.exe's directory. Global rules are overridden by local rules with the same name.

A settings line starts with a sprite name or a name pattern, followed by one or more settings. Empty lines and comments are ignored. For example:

    // This is a comment. The next lines contain sprite settings:
    *            dither-scale: 0.5
    *.at         transparency-color: 0 0 255
    fire         type: oriented      dithering: none
    *.pdn        converter: '"C:\Tools\PdnToPngConverter.exe"'       arguments: '/in="{input}" /out="{output}"'
This sets the dither-scale to 0.5 for all sprites, and it tells SpriteMaker to treat blue (0 0 255) as transparent for all images whose filename contains '.at' (the alpha-transparency setting shorthand). It also sets the sprite type for the image named 'fire' to oriented, and disables dithering for that image. Finally, it tells SpriteMaker to call a converter application for each .pdn file in the image directory - SpriteMaker will then use the output image(s) produced by that application.

If there are multiple matching rules, all of their settings will be applied in order of appearance. In the above example, a sprite named `fire` will use a dither-scale of 0.5 (because of the `*` rule) but dithering will also be disabled for it (because of the `fire` rule). If the `fire` rule would also have specified a dither-scale, then that dither-scale would have been used instead, because the `fire` rule comes after the `*` rule.

SpriteMaker keeps track of settings history in a `spritemaker.dat` file. This enables it to only update sprites whose settings have been modified (if `-full` mode is not enabled).

#### spritemaker.config settings

Sprite settings (for multi-frame sprites, the settings of the first frame are used):

- **type: sprite-type** - Sprite type must be `parallel-upright`, `upright`, `parallel`, `oriented` or `parallel-oriented` (or any of the shorthands: `pu`, `u`, `p`, `o` or `po`). The default type is 'parallel'.
- **texture-format: texture-format** - Texture format must be `normal`, `additive`, `index-alpha` or `alpha-test` (or any of the shorthands: `n`, `a`, `ia` or `at`). The default format is 'additive'.
- **ignore: true/false** - When true, matching files will be ignored. This can be used to exclude certain files or file types from the input directory.
- **preserve-palette: true/false** - When true, input images that are already in an 8-bit indexed format will not be quantized - their palette will be used as-is. No special sprite-type specific handling will be performed. For animated sprites, all input images should be indexed using the same palette.

Frame settings:

- **frame-offset: x y** - The offset of the frame relative to the sprite's center. x and y must be whitespace-separated numbers. Positive x values move the frame towards the right, positive y values move it upwards. The defaults to 0 0, which centers the frame at the sprite's center.

Dithering:

- **dithering: type** - Type must be either `none` or `floyd-steinberg`. By default, Floyd-Steinberg dithering is applied for single-frame sprites, and dithering is disabled for multi-frame sprites.
- **dither-scale: scale** - Scale must be a value between 0 (disables dithering) and 1 (full error  diffusion). The default is 0.75, which softens the effect somewhat.

Alpha-test settings:

- **transparency-threshold: threshold** - Threshold must be a value between 0 and 255. The default is 128. Any pixel whose alpha value is below this threshold will be marked as  transparent.
- **transparency-color: r g b** - A color, written as 3 whitespace-separated numbers, with each number between 0 and 255. Pixels with this color will be marked as transparent.

Index-alpha settings:

- **transparency-input: mode** - Type must be either `alpha` or `grayscale`. By default, alpha is used: pixels with higher alpha values will be more  visible in game. When grayscale is used, pixels that are whiter will be more visible in game.
- **color: r g b** - The index-alpha sprite color, written as 3 whitespace-separated numbers, with each number between 0 and 255. By default, the average color of the image is used.

Conversion settings:

- **converter: 'path'** - The path of an application that can convert a file into one or more image files. If the path contains spaces then it should be surrounded by double quotes. The whole path, including any double quotes, must be delimited by single quotes. Any single quotes in the path itself must be escaped with a `\`. For example, the path `C:\what's that.exe` should be written as `'"C:\what\'s that.exe"'`.
- **arguments: 'arguments'** - The arguments that will be passed to the converter application, surrounded by single quotes. The arguments must contain an input and output placeholder (see below). As with the converter setting, the whole arguments list must be delimited by single quotes, and any path that contains spaces should be surrounded by double quotes. The following placeholders can be used:
  - `{input}` - The full path of the file that will be converted, for example: `C:\HL\mymod\sprites\smoke.ase.`
  - `{input_escaped}` - Same as `{input}`, but with escaped backslashes: `C:\\HL\\mymod\\sprites\\smoke.ase`.
  - `{output}` - The full path of where SpriteMaker expects to find the output file(s), without extension. For example: `C:\HL\mymod\sprites\converted_12345678-9abc-def0-1234-56789abcdef0\smoke`.
  - `{output_escaped}` - Same as `{output}`, but with escaped backslashes: `C:\\HL\\mymod\\sprites\\converted_12345678-9abc-def0-1234-56789abcdef0\\smoke`.

## About Half-Life sprites
Half-Life sprites use a 256-color palette. Their maximum size is 512x512, but unlike textures their width and height do not need to be multiples of 16. The maximum number of frames is technically almost unlimited, but Half-Life will only display the first 256 for most sprites. Sprite filename matching is case-insensitive ('aa.spr' and 'AA.spr' will match the same file). There does not seem to be a clear limit to how large a sprite file can be in terms of filesize.

Note that sprite files do not store color profile information, and because Half-Life does not appear to apply gamma correction properly on all systems, sprites (especially dark ones) may look too bright on some systems.

### Sprite orientations
SpriteMaker lets you select a sprite's orientation by adding including its selector in an image's filename, for example: `smoke.o.png` contains `.o`, which is the shorthand selector for 'Oriented'. A sprite can have one of the following orientations:

- **Parallel-upright** - A sprite that always faces the camera, but is locked along the z-axis. Filename selector: `.pu` or `.parallel-upright`.
- **Upright** - Similar to 'Parallel-upright', but faces the player's origin instead of the camera. Filename selector: `.u` or `.upright`.
- **Parallel** - A sprite that always faces the camera. **Most sprites use this orientation.** Because this is the default orientation, its filename selector (`.p` or `.parallel`) can be left out.
- **Oriented** - A sprite with a fixed orientation that can be set in the level editor. Filename selector: `.o` or `.oriented`.
- **Parallel-oriented** - Similar to 'Oriented', but the sprite will also face the camera. Filename selector: `.po` or `.parallel-oriented`.

### Sprite texture formats
SpriteMaker also uses filename selectors to set a sprite's texture format. A sprite can use one of the following texture formats:

- **Normal** - A 256-color sprite, with no support for transparency. However, it actually behaves the same as the 'Additive' format. Filename selector: `.n` or `.normal`.
- **Additive** - A 256-color sprite, where the brightness of each pixel determines how transparent it is. Black pixels are fully transparent, white pixels are fully opaque. However, this behavior only works when a sprite entity uses the 'additive' render mode. **Most sprites use this format.** Because this is the default texture format, its filename selector (`.a` or `.additive`) can be left out.
- **Index-alpha** - A 1-color sprite, with 256 levels of transparency. This is similar to how decal textures work. Filename selector: `.ia` or `.index-alpha`.
- **Alpha-test** - A 255-color sprite, with support for transparency. Pixels are either fully opaque or fully transparent. This is similar to how transparent textures work. Filename selector: `.at` or `.alpha-test`.

## Custom converters
SpriteMaker can be configured to use custom converters for certain images. This makes it possible to achieve better visual results, or to handle file types that SpriteMaker does not support directly. IrfanView is particularly useful in this regard, but any other command-line program can be used, as long as both the input and output path can be provided as arguments. It's a good idea to put conversion rules in the global `spritemaker.config` file, so they don't need to be repeated in every directory's `spritemaker.config` file.

### Using IrfanView for color conversion
To use IrfanView to convert images to 256 colors, add the following line to your `spritemaker.config` file:

    spritename      converter: '"C:\Program Files\IrfanView\i_view64.exe"' arguments: '"{input}" /silent /bpp=8 /convert="{output}.png"'
Or, when using advanced batch settings, save the right IrfanView batch settings to an `i_view64.ini` file, and specify the directory in which that ini file is located:

    spritename      converter: '"C:\Program Files\IrfanView\i_view64.exe"' arguments: '"{input}" /silent /ini="C:\custom_irfanview_settings_dir" /advancedbatch /convert="{output}.png"'
To use this conversion for multiple images, replace `spritename` with a wildcard pattern such as `if_*`, so any images whose name starts with `if_` will be converted using IrfanView.

### Using pngquant for color conversion

To use pngquant to convert images to 256 colors, add the following line to your `spritemaker.config` file:

    texturename     converter: '"C:\HL\tools\pngquant\pngquant.exe"'    arguments: '"{input}" --output "{output}.png"'

### Converting Gimp files
To automatically convert Gimp files, add the following line to your `spritemaker.config` file:

    *.xcf       converter: '"C:\Program Files\GIMP 2\bin\gimp-console-2.10.exe"' arguments: '-nidc -b "(let* ((image (car (gimp-file-load RUN-NONINTERACTIVE """{input_escaped}""" """{input_escaped}"""))) (layer (car (gimp-image-merge-visible-layers image CLIP-TO-IMAGE)))) (gimp-file-save RUN-NONINTERACTIVE image layer """{output_escaped}.png""" """{output_escaped}.png""") (gimp-image-delete image) (gimp-quit 1))"'

This uses Gimp's command-line Script-Fu batch interpreter to open the specified image, merge all its visible layers and save the result to the conversion output location. SpriteMaker then reads the resulting png file and uses it to create a sprite.

### Converting Aseprite files
To automatically convert Aseprite files, add the following lines to your `spritemaker.config` file:

    *.ase           converter: '"C:\Applications\Aseprite\aseprite.exe"' arguments: '-b "{input}" --save-as "{output}.1.png"'
    *.aseprite      converter: '"C:\Applications\Aseprite\aseprite.exe"' arguments: '-b "{input}" --save-as "{output}.1.png"'
The `-b` switch prevents Aseprite from starting its UI, and the `{output}.1.png` part tells Aseprite to create a separate numbered image for each frame. SpriteMaker then reads all of these images and uses them to create a multi-frame sprite. See [Aseprite Command Line Interface ](https://www.aseprite.org/docs/cli/) for more information about using Aseprite from the command-line.

## Credits
- Thanks to [Yuraj](https://yuraj.ucoz.com) for his unofficial sprite file format specification.
- Thanks to [The303](http://www.the303.org/) for his information about sprite orientations and texture formats.
- SpriteMaker uses the [ImageSharp](https://github.com/SixLabors/ImageSharp) library, which is licensed under the Apache License 2.0.