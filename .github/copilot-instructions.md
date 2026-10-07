# Copilot Instructions

## Project Guidelines
- Keep window/view code-behind logic minimal; put command parsing and completion behavior in a dedicated standalone parser/engine component, with UI forwarding input and rendering state.
- Separate responsibilities into focused components; avoid monolithic classes or modules. Keep component and project boundaries proportional to distinct responsibilities rather than adding abstractions without a concrete need.