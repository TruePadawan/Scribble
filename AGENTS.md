# Repository Guidelines

## Project Structure

Scribble is a Windows and Linux whiteboard built with .NET 10, Avalonia, and SkiaSharp.

- `Scribble/` contains views, view models, pointer tools, canvas services, and UI assets under `Assets/`.
- `Scribble.Desktop/` starts the desktop app and contains desktop packaging assets.
- `Scribble.Server/` hosts the SignalR drawing and chat hub. `Scribble.Shared/` holds the drawing events, canvas models, and serialization types used by both client and server.
- `Scribble.UnitTests/` covers individual components. `Scribble.IntegrationTests/` covers drawing flows and client-server collaboration.

## Build and Run

Use the .NET 10 SDK. Run solution commands from the repository root.

- `dotnet restore Scribble.slnx` restores NuGet dependencies. Manage package versions in `Directory.Packages.props`.
- `dotnet build Scribble.slnx` builds all projects.
- Run `dotnet run` from `Scribble.Desktop/` to launch the whiteboard. For live drawing, start `Scribble.Server/` first with `dotnet run` and check the client endpoint in `Scribble/appsettings.Development.json`.

## Coding Style

Use four spaces, file-scoped namespaces, and braces on separate lines in C#. Always use opening and closing braces around control flow bodies that allow them, including `if`, `else`, and loops, even when the body contains a single statement. Use PascalCase for types and members, camelCase for locals and parameters, and `_camelCase` for private fields. Follow neighboring code and the checked-in `Scribble.sln.DotSettings` when formatting. Keep Avalonia control access on the UI thread and preserve compatibility when changing shared drawing events or document serialization.

Use a brief C# XML documentation comment (`/// <summary>`) above each new class, method, function, or similar declaration to explain its purpose. Inside methods and functions, add short comments where a section's purpose or behavior needs clarification.

## Testing

Run `dotnet test --solution Scribble.slnx` with Microsoft Testing Platform. Use xUnit v3 and `[AvaloniaFact]` for tests that need Avalonia. Name tests after the method, scenario, and expected result, such as `SaveAsync_BackgroundColorIsSaved`. Add regression coverage for changed behavior. The release workflow runs tests on Linux and Windows without a coverage threshold.

## Commits and Pull Requests

Follow the short, imperative subjects common in the history, using prefixes such as `fix:`, `feat:`, `refactor:`, or `chore:`. Keep pull requests focused. Describe the user-visible change and verification, link related issues when available, and include screenshots for visual changes.
