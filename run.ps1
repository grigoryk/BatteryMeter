dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -v quiet
Start-Process "$PSScriptRoot\bin\Release\net10.0-windows\win-x64\publish\BatteryMeter.exe"
