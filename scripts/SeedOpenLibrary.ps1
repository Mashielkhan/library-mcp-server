param(
	[int]$TargetCount = 1000
)

$env:OPEN_LIBRARY_TARGET_COUNT = $TargetCount
dotnet run --project "$PSScriptRoot/../src/LibraryMCPServer/LibraryMCPServer.csproj" -- --seed-open-library