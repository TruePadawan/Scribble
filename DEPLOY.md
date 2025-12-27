### Linux
Publish for Linux  
`dotnet publish "./Scribble.Desktop/Scribble.Desktop.csproj"   --verbosity quiet   --nologo   --configuration Release   --self-contained true   --runtime linux-x64   --output "./out/linux-x64" /p:PublishSingleFile=true`  

The command below turns the published files into a .deb file  
`dpkg-deb --root-owner-group --build <staging-folder> "./scribble_1.0.0_amd64.deb"`

### Windows
Publish for Windows  
`dotnet publish "./Scribble.Desktop/Scribble.Desktop.csproj"   --verbosity quiet   --nologo   --configuration Release   --self-contained true   --runtime win-x64   --output "./out/win-x64" /p:PublishSingleFile=true`