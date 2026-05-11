$platforms = @{ name = 'win64';         runtime = 'win-x64'   },
             @{ name = 'win32';         runtime = 'win-x86'   },
             @{ name = 'linux64';       runtime = 'linux-x64' },
             @{ name = 'osx64';         runtime = 'osx-x64'  }

# Build all platform variants:
foreach ($platform in $platforms) {
    # NOTE: dotnet publish doesn't handle .pubxml files correctly, hence all the extra arguments here:
    dotnet publish .\WadMaker\WadMaker.csproj -c Release -f net8.0 -r $($platform.runtime) --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true --output .\WadMaker\bin\Release\net8.0\publish\$($platform.runtime)
    dotnet publish .\SpriteMaker\SpriteMaker.csproj -c Release -f net8.0 -r $($platform.runtime) --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true --output .\SpriteMaker\bin\Release\net8.0\publish\$($platform.runtime)
    dotnet publish .\ModelTextureMaker\ModelTextureMaker.csproj -c Release -f net8.0 -r $($platform.runtime) --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true --output .\ModelTextureMaker\bin\Release\net8.0\publish\$($platform.runtime)
}

# Create release zip files:
$version = (Get-Item .\WadMaker\bin\Release\net8.0\publish\win-x64\WadMaker.exe).VersionInfo.ProductVersion
foreach ($platform in $platforms) {
    # Create output directory:
    $output_dir = ".\Releases\${version}\WadMaker_${version}_$($platform.name)"
    New-Item -ItemType Directory -Force -Path $output_dir

    # Copy files (executables, config files, examples, documentation, 3rd party licenses):
    Copy-Item -Path .\WadMaker\bin\Release\net8.0\publish\$($platform.runtime)\* -Destination $output_dir
    Copy-Item -Path .\SpriteMaker\bin\Release\net8.0\publish\$($platform.runtime)\* -Destination $output_dir
    Copy-Item -Path .\ModelTextureMaker\bin\Release\net8.0\publish\$($platform.runtime)\* -Destination $output_dir
    Copy-Item -Path .\files\* -Destination $output_dir
    Copy-Item -Path '.\ImageSharp LICENSE' -Destination $output_dir
    Copy-Item -Path '.\ImageSharp THIRD-PARTY-NOTICES.TXT' -Destination $output_dir

    # TODO: Maybe create platform-specific 'files' folders?
    if (!$($platform.name).Contains("win")) {
        Remove-Item -Path "$output_dir\*.bat"
    }

    # Zip it:
    Compress-Archive -Force -CompressionLevel Optimal -Path $output_dir -DestinationPath "${output_dir}.zip"
}