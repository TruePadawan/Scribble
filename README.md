## Scribble

<img width="1916" height="1012" alt="Screenshot_2026-01-03_20-23-46" src="https://github.com/user-attachments/assets/5dd9b889-a7a6-43ce-9458-c0ba9542b422" />

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
