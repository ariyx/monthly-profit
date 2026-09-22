# Contributing

Thank you for improving Monthly Profit. This project is a Windows WPF application with a small .NET core library and a console-based test project.

## Prerequisites

- Windows 10 or later for running the desktop application.
- .NET 10 SDK.

## Development workflow

1. Create a focused branch from `main`.
2. Keep application changes limited to the relevant layer: `Core/` for business logic and persistence, `Desktop/` for the WPF interface, and `Tests/` for executable checks.
3. Add or update tests when changing calculation, persistence, or spreadsheet behavior.
4. Run the release-mode test command before opening a pull request:

   ```text
   dotnet run --project Tests/Profit.Tests.csproj --configuration Release
   ```

5. For a release-package change, run the Windows-only packaging script in PowerShell:

   ```powershell
   .\build-windows.ps1
   ```

## Data and security

Do not commit customer data, local SQLite databases, backups, generated packages, or secrets. The application has no environment-variable configuration or service credentials.

Treat changes to sales calculations, database migrations, import handling, and backup/restore behavior as high risk. Preserve backward compatibility where practical and test realistic data before distributing a package.

## Pull requests

In the pull request description, explain the user-facing change, note any data or migration impact, and include the validation commands you ran. Do not commit generated files from `bin/`, `obj/`, or `out/`.

## License

No project license is currently declared. If you intend to contribute code, confirm the applicable contribution and distribution terms with the repository owner first.
