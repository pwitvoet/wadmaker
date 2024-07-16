# WadMaker & SpriteMaker

*"With my brains and your brawn we'll make an excellent team."*



## Introduction

WadMaker and SpriteMaker are command-line tools for creating Half-Life wad and sprite files. They are designed to be:

**Efficient:** convert a directory full of images to a wad file or to a directory full of sprites with just a single action. Existing wad files and sprite directories can be updated more quickly because only changes are processed.

**Convenient:** no need to fiddle with palettes and special transparency colors. True-color input images are automatically converted to 256 colors, with a palette that matches the type of the texture or sprite.

**Flexible:** png, jpg, gif, bmp and tga files are supported, as well as Photoshop (psd and psb) and Krita files (kra and ora). Support for other formats, such as Gimp and Aseprite, can be enabled by configuring external conversion tools.

**Configurable:** common settings such as sprite orientation and format can be configured with filenames. Less common settings can be set in plain-text config files.



## Getting started

Start by downloading the latest version here: [WadMaker_SpriteMaker_v1.2.zip](https://github.com/pwitvoet/wadmaker/releases/download/1.2/WadMaker_SpriteMaker_1.2.zip).

Extract the WadMaker_SpriteMaker zip file somewhere on your computer, then drag a directory full of images onto WadMaker.exe or SpriteMaker.exe to create a wad file or a directory full of sprites. Or, if you want to automatically save the wad or sprite files in the right location, create a batch file that calls the right tool with two arguments: first the input directory and then the output location. The download package contains example batch files to get you started.



## Documentation

For more information, see the [WadMaker documentation](wadmaker.md) or [SpriteMaker documentation](spritemaker.md).

