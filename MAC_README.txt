In order to run these apps you must unquarantine them! This is automatically applied to all executables downloaded from the internet and can only be removed on our side via a paid process with Apple (which we have not done).

You may remove the quarantine in the terminal like so after extracting the apps:
xattr -d com.apple.quarantine WadMaker
xattr -d com.apple.quarantine SpriteMaker
xattr -d com.apple.quarantine ModelTextureMaker

If you are not comfortable using the terminal, you may try this helper: https://fluffy.itch.io/dequarantine

After this, you may run the apps from the command line as normal, e.g. ./WadMaker /path/to/some/textures

You may wish to move the apps somewhere on the path such as /usr/local/bin so they can be used from anywhere.

You can read more about macOS quarantines here: https://www.isscloud.io/guides/macos-security-and-com-apple-quarantine-extended-attribute/