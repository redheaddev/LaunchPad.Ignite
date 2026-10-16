# Copilot Instructions

## Project Guidelines
- Keep window/view code-behind logic minimal; put command parsing and completion behavior in a dedicated standalone parser/engine component, with UI forwarding input and rendering state.
- Separate responsibilities into focused components; avoid monolithic classes or modules. Keep component and project boundaries proportional to distinct responsibilities rather than adding abstractions without a concrete need.

## Command Structure
- Declare an extension's command namespace once at the manifest root and apply it to every command in that extension.

## Project-Specific Rules
- When updating parser rules, update `LaunchPad.Ignite.App/rules-parser.txt` rather than `.github/copilot-instructions.md`; the former is the intended parser rules file.
- When implementing alphabetical sorting requested for displayed lists, apply it consistently to every user-visible list in the app, rather than only reordering one source file's definitions.
- Use `ListSelectionMode.Single` and `ListSelectionMode.Multiple` rather than `Minimal` and `Normal`; enum names should describe selection cardinality.
- When packaging a new install, bump the application version before building the installer; use the next patch version unless specified otherwise.

## Security Guidelines
- Enforce extension secret access using explicit per-secret, per-extension user grants in settings, with a generic mechanism and default-deny behavior; never grant extension access unless the user explicitly permits it.

## PowerShell Command Guidelines
- Prefer a single semicolon-separated command line or separate commands one at a time when pasting multiline blocks containing multiple PowerShell commands in Developer PowerShell 7.
