param([int]$Port = 54331, [ValidateRange(1,2)][int]$Rounds = 2, [switch]$Serve)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$previousSeed = $env:SeedUsers__Password
$previousConnection = $env:ConnectionStrings__SecureLab
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousPort = $env:POSTGRES_PORT
Push-Location $projectRoot
try {
    $env:POSTGRES_PORT = [string]$Port
    & docker compose --project-name securelab-lab4-verification --env-file infra/.env.example -f infra/compose.yaml up -d --wait
    if ($LASTEXITCODE -ne 0) { throw 'Verification PostgreSQL failed to start.' }
    $env:ConnectionStrings__SecureLab = "Host=127.0.0.1;Port=$Port;Database=securelab;Username=securelab;Password=local-study-password;Include Error Detail=false"
    if (-not $env:SeedUsers__Password) { $env:SeedUsers__Password = 'Aa1!' + [Guid]::NewGuid().ToString('N') }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    & dotnet restore SecureLab.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & dotnet build SecureLab.sln -c Release --no-restore --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    for ($round = 1; $round -le $Rounds; $round++) {
        Write-Output "Round ${round}: reset isolated lab4 database and run full suite"
        & dotnet run --project src/SecureLab.Api -c Release --no-build -- --reset-database
        if ($LASTEXITCODE -ne 0) { throw 'Reset failed.' }
        & dotnet test tests/SecureLab.Api.Tests -c Release --no-build --no-restore --logger "trx;LogFileName=lab04-round-$round.trx" --results-directory TestResults
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }
    if ($Serve) {
        & dotnet run --project src/SecureLab.Api -c Release --no-build -- --reset-database
        if ($LASTEXITCODE -ne 0) { throw 'Final reset failed.' }
        Write-Output 'SecureLab: http://localhost:5084/auth-lab.html . Use register if no seed password was supplied.'
        & dotnet run --project src/SecureLab.Api -c Release --no-build --no-launch-profile --urls http://localhost:5084
    }
}
finally {
    $env:SeedUsers__Password = $previousSeed
    $env:ConnectionStrings__SecureLab = $previousConnection
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:POSTGRES_PORT = $previousPort
    Pop-Location
}
