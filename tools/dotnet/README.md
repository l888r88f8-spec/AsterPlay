# Portable .NET SDK

AsterPlay's local build script checks this directory before the system-wide `dotnet`.

For a portable build machine, place a complete Windows x64 .NET 8 SDK installation here so this file exists:

```text
tools/dotnet/dotnet.exe
```

The published `dist/AsterPlay/` folder is self-contained, so PCs that only run AsterPlay do not need .NET installed.
