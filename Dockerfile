FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Scribble.Server/Scribble.Server.csproj", "Scribble.Server/"]
COPY ["Scribble.Shared/Scribble.Shared.csproj", "Scribble.Shared/"]
COPY ["Directory.Packages.props", "."]

RUN dotnet restore "Scribble.Server/Scribble.Server.csproj"

COPY . .

WORKDIR "/src/Scribble.Server"
RUN dotnet publish "Scribble.Server.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# Server-side board replay builds text paths using the system font fallback.
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_HTTP_PORTS=8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Scribble.Server.dll"]