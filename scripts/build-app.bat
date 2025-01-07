@rem off

dotnet publish AutoYnabCsv.DesktopApp -r win-x64 -p:PublishSingleFile=true
dotnet publish AutoYnabCsv.DesktopApp -r win-arm64 -p:PublishSingleFile=true

rem dotnet msbuild AutoYnabCsv.DesktopApp -t:BundleApp -property:Configuration=Release -p:RuntimeIdentifier=win-x64 -p:UseAppHost=true
rem dotnet msbuild AutoYnabCsv.DesktopApp -t:BundleApp -property:Configuration=Release -p:RuntimeIdentifier=win-arm64 -p:UseAppHost=true
