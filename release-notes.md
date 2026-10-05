# Recent Changes

- Faster solving through incremental optimization, smaller symmetry-aware coverage constraints, and improved placement conflict encoding - faster across tested scenarios by ~90%, about 10x faster overall.
- Updated bundled CryptoMiniSat to 5.16.0
- Increased the template size limit to 60 to account for 4-way symmetry 3-clip now being feasible above 50.

<!-- install-start -->

## Installation

### From The Depths Mod

Just like any other FtD mod - subscribe to the [APS Generator Mod](https://steamcommunity.com/sharedfiles/filedetails/?id=3808824333) on the Steam Workshop, install it through the FtD mod UI, and reload the game to enable the mod. Menu is accessible through a button beside prefab controls or by pressing F7.

### Standalone UI Version

#### Windows

1. Download `APS-Generator-Setup.exe`.
2. Run the installer.
3. When Windows shows "Windows protected your PC", click **More Info** → **Run Anyway**.
4. The app will install and launch automatically. A desktop shortcut is created.

> **Why does Windows block the program?** The executable is unsigned - it doesn't have a certificate identifying its publisher. This is harmless; Windows just can't verify the source automatically.

#### Linux

1. Download `APS-Generator.AppImage`.
2. Make it executable and run:
   ```bash
   chmod +x APS-Generator.AppImage && ./APS-Generator.AppImage
   ```

> **Modern Linux distributions (Arch, Ubuntu, Debian, etc.):** for now (may be fixed later), the generated AppImage requires FUSE2. Install it with:
>
> ```bash
> sudo pacman -S fuse2
> ```

<!-- install-end -->
