## Scribble

### Building for Android

If .NET cannot find the SDKs (Android and Java):  
Paste the following into the Directory.Build.props file (create if it doesn't exist) in the same folder as the .sln file

```xml 

<Project>
    <PropertyGroup>
        <AndroidSdkDirectory>/home/hermes/android-sdk</AndroidSdkDirectory>
        <JavaSdkDirectory>/home/hermes/jdk</JavaSdkDirectory>
    </PropertyGroup>
</Project>
```