# Steam File Importer

A native Windows desktop application for importing `.manifest` and `.lua` files into your Steam installation directory.

## Features

- **Auto-detect Steam** - Automatically finds your Steam installation from the Windows registry
- **Local File Import** - Select `.manifest` and `.lua` files from your computer and copy them to the correct Steam folders (`depotcache` and `config\stplug-in`)
- **Online Search & Download** - Search Steam games by name or AppID and download manifest/lua files directly via the Ryuu API
- **Restart Steam** - One-click button to gracefully restart Steam (shutdown + relaunch)
- **Dark Mode UI** - Clean, modern dark theme built with WPF

## Requirements

- Windows 10/11
- .NET 9.0 Runtime (or build from source with .NET 9 SDK)

## Build from Source

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

The standalone `.exe` will be in the `dist/` folder.

## Usage

1. Launch the application - it will automatically detect your Steam directory
2. Use **Local File Import** tab to manually select and import `.manifest` and `.lua` files
3. Use **Online Search & Downloader** tab to search games and download files from Ryuu
4. Click **Restart Steam** in the bottom bar to restart Steam after importing files
