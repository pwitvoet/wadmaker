REM This is an example batch file that uses SpriteMaker to convert all images in 'C:\HL\mymod\sprites' and its sub-directories
REM into sprites in the 'C:\Program Files (x86)\Steam\steamapps\common\Half-Life\mymod\sprites' directory.
REM The -subdirs option causes SpriteMaker to also process sub-directories, creating a matching directory hierarchy in the output directory.
REM The -subdirremoval option causes SpriteMaker to remove output sub-directories if the corresponding input sub-directory is removed.

REM To use this batch file, replace the directory paths below with the right paths for your system, and then remove the 'REM ' from the line below:
REM "C:\HL\tools\SpriteMaker.exe" -subdirs -subdirremoval "C:\HL\mymod\sprites" "C:\Program Files (x86)\Steam\steamapps\common\Half-Life\mymod\sprites"

PAUSE