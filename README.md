## Scribble
A cross-platform (windows/linux) infinite whiteboard

### Tech Stack
- [C#/.NET](https://dotnet.microsoft.com/en-us/): Core
- [Avalonia UI](https://avaloniaui.net): For consistent cross-platform user interface  
- [Icons8](https://icons8.com): Icons
- [SkiaSharp](https://skiasharp.com): Graphics rendering
- [SignalR](https://dotnet.microsoft.com/en-us/apps/aspnet/signalr): Collaborative drawing
- [Render + Docker](https://render.com/): Hosting the SignalR server

<img width="1895" height="1027" alt="Screenshot from 2026-05-03 13-10-20" src="https://github.com/user-attachments/assets/6e9d47bf-ddd0-4f55-bc52-2d676a30942d" />

<img width="1895" height="1027" alt="Screenshot from 2026-05-03 13-16-59" src="https://github.com/user-attachments/assets/7017ae99-cd33-41a3-80fe-ba67734b45d5" />

## Local Setup

### Prerequisites
* [.NET SDK](https://dotnet.microsoft.com/download) (Version 8.0 or newer recommended)

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
3. If you want to use multi-user drawing, you have to run the Scribble.Server project first (can just set up multi project launch on whatever IDE you're using)
   ```bash
   cd Scribble.Server
   dotnet run
   ``` 
