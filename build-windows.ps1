$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
function CheckExit([string]$Step) { if ($LASTEXITCODE -ne 0) { throw "$Step failed ($LASTEXITCODE)" } }
dotnet run --project Tests/Profit.Tests.csproj --configuration Release
CheckExit 'Business, database and spreadsheet tests'
dotnet publish Desktop/Profit.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:PublishTrimmed=false -p:EnableCompressionInSingleFile=true --output out/publish
CheckExit 'Windows publish'
New-Item -ItemType Directory -Force out/release | Out-Null
Copy-Item out/publish/MonthlyProfit.exe out/release/MonthlyProfit-Setup.exe -Force
Copy-Item USER-GUIDE.fa.txt out/release/ -Force
Get-FileHash out/release/MonthlyProfit-Setup.exe -Algorithm SHA256 | Format-List | Out-File out/release/SHA256.txt
Compress-Archive -Path out/release/* -DestinationPath out/MonthlyProfit-Windows-x64.zip -Force
Write-Host 'BUILD COMPLETE. Windows 10/11 interactive installation acceptance tests are still required.'
