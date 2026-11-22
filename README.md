## Scribble

A cross-platform (windows/linux) infinite whiteboard

### Tech Stack

- [C#/.NET](https://dotnet.microsoft.com/en-us/): Core (.NET 10)
- [Avalonia UI](https://avaloniaui.net): For consistent cross-platform user interface (v12)
- [Icons8](https://icons8.com): Icons
- [SkiaSharp](https://skiasharp.com): Hardware-accelerated graphics rendering
- [SignalR](https://dotnet.microsoft.com/en-us/apps/aspnet/signalr): Collaborative drawing
- [Render + Docker](https://render.com/): Hosting the SignalR server

<img width="1897" height="1025" alt="image" src="https://github.com/user-attachments/assets/86154d42-f365-4b0d-8da4-fb0cfffef199" />

<img width="1895" height="1027" alt="Screenshot from 2026-05-03 13-10-20" src="https://github.com/user-attachments/assets/6e9d47bf-ddd0-4f55-bc52-2d676a30942d" />

<img width="1895" height="1027" alt="Screenshot from 2026-05-03 13-16-59" src="https://github.com/user-attachments/assets/7017ae99-cd33-41a3-80fe-ba67734b45d5" />

## Local Setup

### Prerequisites

* [.NET SDK](https://dotnet.microsoft.com/download) (Version 10.0 required)

### Running the Application

To run Scribble locally, you will need to start both the backend server and the client application.

1. **Clone the repository and restore the packages:**
   ```bash
   git clone https://github.com/TruePadawan/Scribble.git
   cd Scribble
   dotnet restore
   ```
2. **Run the program**
   ```bash
   cd Scribble.Desktop
   dotnet run
   ```
3. If you want to use multi-user drawing, you have to run the Scribble.Server project first (can just set up multi
   project launch on whatever IDE you're using)
   ```bash
   cd Scribble.Server
   dotnet run
   ```

### Running Tests

To execute the automated test suite locally via the new Microsoft Testing Platform:

```bash
dotnet test Scribble.slnx
```

### Drawing room deployment

Run a single Scribble.Server instance for clients that share rooms. Each host keeps room history in memory and allows
participants to reclaim their strokes for ten minutes after leaving. Deploy the v2 desktop client and server together.
See the [drawing room protocol](docs/room-protocol.md) for concurrent editing behavior and snapshot measurements.

### Offline use and room configuration

The Windows and Linux desktop builds require the .NET 10 SDK.

The desktop app reads the settings beside its executable when you join a room. Set `ServerUrl` in `appsettings.json` to
the full HTTP or HTTPS drawing hub URL. Debug builds also read the optional `appsettings.Development.json` override.
Local drawing, opening, saving, and recovery work without either file.
