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

## Build installers

On Windows, install Inno Setup 6 or 7 and run the repository script from the root directory:

```powershell
./Installer/build-installer.ps1 -RuntimeIdentifier both
```

The script publishes and builds installers for `win-x64` and `win-arm64`, placing them in `Installer/artifacts`. It also copies the x64 installer to `C:\temp\LaunchPad.Ignite.Install`.

The installer release process expects the app version to be bumped before building. See `.github/copilot-instructions.md` for repository-specific packaging requirements.

## Project layout

- `LaunchPad.Ignite.App` — Avalonia desktop app and application services.
- `LaunchPad.Ignite.Core` — command parsing, execution, and shared domain types.
- `LaunchPad.Ignite.Tests` — automated tests.
- `Installer` — Inno Setup script and installer build script.
