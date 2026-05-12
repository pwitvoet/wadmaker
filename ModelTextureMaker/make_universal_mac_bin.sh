mkdir -p publish/osx-universal
lipo -create \
  publish/osx-x64/ModelTextureMaker \
  publish/osx-arm64/ModelTextureMaker \
  -output publish/osx-universal/ModelTextureMaker