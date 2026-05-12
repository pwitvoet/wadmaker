mkdir -p publish/osx-universal
lipo -create \
  publish/osx-x64/WadMaker \
  publish/osx-arm64/WadMaker \
  -output publish/osx-universal/WadMaker