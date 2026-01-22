## Scribble

<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/b48e97ab-129e-4466-8df5-fdab73ac8b98" />

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
