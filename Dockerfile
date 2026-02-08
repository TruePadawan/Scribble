FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Scribble.Server/Scribble.Server.csproj", "Scribble.Server/"]
COPY ["Scribble.Shared/Scribble.Shared.csproj", "Scribble.Shared/"]

RUN dotnet restore "Scribble.Server/Scribble.Server.csproj"

COPY . .

WORKDIR "/src/Scribble.Server"
RUN dotnet publish "Scribble.Server.csproj" -c Release -p /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_HTTP_PORTS=8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Scribble.Server.dll"]