param([int]$Port = 54330, [switch]$Serve)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
$previousSeed = $env:SeedUsers__Password
$previousConnection = $env:ConnectionStrings__SecureLab
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousPort = $env:POSTGRES_PORT
try {
    # A separate Compose project/volume; reset never touches the normal lab database.
    $env:POSTGRES_PORT = [string]$Port
    & docker compose --project-name securelab-lab3-verification --env-file infra/.env.example -f infra/compose.yaml up -d --wait
    if ($LASTEXITCODE -ne 0) { throw 'Verification PostgreSQL failed to start.' }
    $env:ConnectionStrings__SecureLab = "Host=127.0.0.1;Port=$Port;Database=securelab;Username=securelab;Password=local-study-password;Include Error Detail=false"
    $env:SeedUsers__Password = 'Aa1!' + [Guid]::NewGuid().ToString('N')
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    & dotnet restore SecureLab.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & dotnet build SecureLab.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    for ($round = 1; $round -le 2; $round++) {
        Write-Output "Verification round ${round}: reset then full test suite"
        & dotnet run --project src/SecureLab.Api -c Release --no-build -- --reset-database
        if ($LASTEXITCODE -ne 0) { throw "Reset $round failed." }
        & dotnet test tests/SecureLab.Api.Tests -c Release --no-build --no-restore --logger "trx;LogFileName=lab03-round-$round.trx" --results-directory TestResults
        if ($LASTEXITCODE -ne 0) { throw "Tests after reset $round failed." }
    }
    if ($Serve) {
        Write-Output 'Verified API: http://localhost:5083/auth-lab.html (use register; seed password is not printed).'
        & dotnet run --project src/SecureLab.Api -c Release --no-build --no-launch-profile --urls http://localhost:5083
    }
}
finally {
    $env:SeedUsers__Password = $previousSeed
    $env:ConnectionStrings__SecureLab = $previousConnection
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:POSTGRES_PORT = $previousPort
    Pop-Location
}
