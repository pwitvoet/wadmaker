mkdir -p publish/osx-universal
lipo -create \
  publish/osx-x64/SpriteMaker \
  publish/osx-arm64/SpriteMaker \
  -output publish/osx-universal/SpriteMaker