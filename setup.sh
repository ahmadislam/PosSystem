#!/usr/bin/env bash
# Run once from this folder: ./setup.sh
# Creates the solution, projects, references and packages, copies in the source from ./overlay,
# builds, runs the tests and generates the first EF Core migration.
set -euo pipefail
cd "$(dirname "$0")"

command -v dotnet >/dev/null || { echo "Install the .NET 10 SDK first: https://dotnet.microsoft.com/download"; exit 1; }
[ -d overlay ] || { echo "overlay/ folder not found - has setup already been run?"; exit 1; }

echo "==> Creating solution and projects"
dotnet new sln -n PosSystem
dotnet new classlib -n PosSystem.Domain         -o src/PosSystem.Domain
dotnet new classlib -n PosSystem.Application    -o src/PosSystem.Application
dotnet new classlib -n PosSystem.Infrastructure -o src/PosSystem.Infrastructure
dotnet new webapi   -n PosSystem.Api            -o src/PosSystem.Api --use-controllers
dotnet new xunit    -n PosSystem.Tests          -o tests/PosSystem.Tests
dotnet sln add src/*/*.csproj tests/*/*.csproj

echo "==> Project references"
dotnet add src/PosSystem.Application    reference src/PosSystem.Domain
dotnet add src/PosSystem.Infrastructure reference src/PosSystem.Application
dotnet add src/PosSystem.Api            reference src/PosSystem.Infrastructure
dotnet add tests/PosSystem.Tests        reference src/PosSystem.Domain

echo "==> NuGet packages"
dotnet add src/PosSystem.Application    package Microsoft.EntityFrameworkCore.Relational
dotnet add src/PosSystem.Application    package Microsoft.Extensions.Identity.Core
dotnet add src/PosSystem.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/PosSystem.Infrastructure package System.IdentityModel.Tokens.Jwt
dotnet add src/PosSystem.Api            package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/PosSystem.Api            package Microsoft.EntityFrameworkCore.Design
# Swashbuckle is pinned to 6.9.0 on purpose (classic Swagger UI API); the template's OpenApi package conflicts with it.
dotnet add src/PosSystem.Api            package Swashbuckle.AspNetCore --version 6.9.0
dotnet remove src/PosSystem.Api         package Microsoft.AspNetCore.OpenApi || true

echo "==> Replacing template files with the project source"
rm -f src/*/Class1.cs src/PosSystem.Api/Program.cs src/PosSystem.Api/WeatherForecast.cs \
      src/PosSystem.Api/Controllers/WeatherForecastController.cs src/PosSystem.Api/*.http \
      tests/PosSystem.Tests/UnitTest1.cs
cp -R overlay/. .
rm -rf overlay

echo "==> Build and test"
dotnet build
dotnet test --no-build

echo "==> EF Core migration"
dotnet tool install --global dotnet-ef 2>/dev/null || dotnet tool update --global dotnet-ef
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet ef migrations add InitialCreate -p src/PosSystem.Infrastructure -s src/PosSystem.Api -o Persistence/Migrations

echo
echo "Done. Start PostgreSQL ('docker compose up -d db'), then:"
echo "  dotnet run --project src/PosSystem.Api --urls http://localhost:5080"
echo "Swagger: http://localhost:5080/swagger   (login: admin / Admin@12345)"
