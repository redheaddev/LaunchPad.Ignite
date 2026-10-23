# LaunchPad.Ignite

LaunchPad.Ignite is a keyboard-driven command launcher built with .NET 8 and Avalonia. It provides a compact mini mode and a full launcher window for searching and running commands, managing command packs, and working with secrets.

## Features

- Search and run built-in and user-defined commands.
- Switch between main and mini launcher modes.
- Manage command packs and add-ons through the app.
- Store secrets and copy selected secret values to the clipboard with automatic expiration. The clipboard is cleared only if its contents have not changed; the countdown can be clicked to add 30 seconds.
- Track command history and configure launcher settings.

## Install

Download the installer for your Windows architecture from the repository's Releases page:

- `LaunchPad.Ignite-Setup_<version>_win-x64.exe` for 64-bit x86 Windows.
- `LaunchPad.Ignite-Setup_<version>_win-arm64.exe` for ARM64 Windows.

Run the installer for a per-user installation. The installer can optionally create a desktop shortcut.

## Use

Launch the app and start typing to search available commands. Press **Enter** to run the highlighted command. Use **Tab** or **Right Arrow** to accept a completion, and **Esc** to close the launcher. The default activation shortcut is **Alt+Space**.

Use the settings and help buttons in the main window for configuration and command guidance. Secret commands prompt for secret names and values as needed. Secret values are not included in command result messages. Avoid copying other sensitive content to the clipboard while a secret countdown is active, since expiration only clears the clipboard when it still contains the copied secret.

## Build and test

Requirements: .NET 8 SDK and Visual Studio 2022 or later (or another supported .NET development environment).

```powershell
dotnet restore LaunchPad.Ignite.sln
dotnet build LaunchPad.Ignite.sln
dotnet test LaunchPad.Ignite.Tests/LaunchPad.Ignite.Tests.csproj
```

## Package all platforms

The `Package all platforms` GitHub Actions workflow builds version-matched packages on native runners for Windows x64 and ARM64, Linux x64, and macOS x64 and ARM64. Run it manually from the Actions tab, optionally entering the version in `LaunchPad.Ignite.App.csproj`. Pushing a `v<version>` tag also triggers packaging. In either case, the version must match the project version, so bump the app version before packaging. The workflow uploads downloadable build artifacts but does not publish a GitHub Release.

The macOS artifact is a ZIP containing a versioned `.app` bundle. The Linux artifact is a portable x64 tarball. Windows artifacts are per-user Inno Setup installers.

## Build platform packages locally

On Windows, install Inno Setup 6 or 7 and run the repository script from the root directory:

```powershell
./Installer/build-installer.ps1 -RuntimeIdentifier both
```

The script publishes and builds installers for `win-x64` and `win-arm64`, placing them in `Installer/artifacts`. It also copies the x64 installer to `C:\temp\LaunchPad.Ignite.Install`.

The installer release process expects the app version to be bumped before building. See `.github/copilot-instructions.md` for repository-specific packaging requirements.

## Build a Linux package

On Linux, build the portable x64 tarball (the default) with the .NET 8 SDK:

```bash
bash ./scripts/package-linux-app.sh
```

To build the ARM64 tarball, pass the project version and `linux-arm64` runtime identifier:

```bash
bash ./scripts/package-linux-app.sh "$(dotnet msbuild LaunchPad.Ignite.App/LaunchPad.Ignite.App.csproj -getProperty:Version -nologo)" linux-arm64
```

The architecture-specific archive is written to `LaunchPad.Ignite.App/artifacts/linux`. Extract it, run `chmod +x ./LaunchPad.Ignite.App`, then launch `./LaunchPad.Ignite.App`. Use the package matching the machine's CPU architecture (`linux-x64` or `linux-arm64`). A graphical desktop session is required. Global activation hotkeys use a keyboard hook in X11 sessions and the desktop's `org.freedesktop.portal.GlobalShortcuts` portal in Wayland sessions. Wayland support depends on the desktop portal implementation and may require approving the shortcut. Wayland shortcut diagnostics are written to `$XDG_STATE_HOME/LaunchPad.Ignite/activation-hotkey.log`, or `~/.local/state/LaunchPad.Ignite/activation-hotkey.log` if `XDG_STATE_HOME` is unset. Linux secret storage is not supported; secret commands report that limitation and do not store values.

On macOS, build a ZIP containing the app bundle for the host architecture, or specify an architecture explicitly:

```bash
bash ./scripts/package-macos-app.sh
bash ./scripts/package-macos-app.sh 1.0.30 osx-arm64
```

## Project layout

- `LaunchPad.Ignite.App` — Avalonia desktop app and application services.
- `LaunchPad.Ignite.Core` — command parsing, execution, and shared domain types.
- `LaunchPad.Ignite.Tests` — automated tests.
- `Installer` — Inno Setup script and installer build script.
- `scripts` — platform-specific app packaging scripts.
