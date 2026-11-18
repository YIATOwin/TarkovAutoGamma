param([string]$Dotnet='dotnet',[string]$Output='')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$Output){$Output=Join-Path $repo 'artifacts\win-x64'}
& $Dotnet run --project (Join-Path $repo 'tests\Regression.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Regression checks failed.'}
& $Dotnet publish (Join-Path $repo 'src\TarkovAutoGamma.csproj') -c Release -r win-x64 -o $Output
if($LASTEXITCODE -ne 0){throw 'Publish failed.'}
Copy-Item -LiteralPath (Join-Path $repo 'README.md'),(Join-Path $repo 'LICENSE'),(Join-Path $repo 'VERIFICATION.md') -Destination $Output
Copy-Item -LiteralPath (Join-Path $repo 'licenses') -Destination $Output -Recurse -Force
$exe=Join-Path $Output 'TarkovAutoGamma.exe'
$hash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $Output 'TarkovAutoGamma.exe.sha256'),"$hash  TarkovAutoGamma.exe`n")
Write-Host "Ready: $Output"
