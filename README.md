# Monthly Profit

Monthly Profit is a Windows desktop application for recording sales and estimating monthly profit. It is designed for a sales-led workflow: each sale is calculated independently from the confirmed monthly rates of its product brand, then reported alongside fixed monthly expenses.

The application interface and the included end-user guide are in Persian. All operational data is stored locally in SQLite; the application does not require a server or cloud account.

## What it does

- Records sales manually or imports them from Excel workbooks.
- Assigns products to brands by product-code prefix.
- Requires monthly brand rates to be reviewed and confirmed before sales for that month are calculated.
- Estimates cost and net profit for each calculated sale, with an optional manual cost override.
- Tracks fixed monthly expenses and provides month and date-range reports.
- Exports reports to `.xlsx`, prints the active month, and supports manual and optional exit-time backups.

This release does not provide purchase, inventory, reconciliation, or FIFO accounting. It is not an inventory-management system.

## Requirements

- Windows 10 or later to run the desktop application.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download) to build, test, or run from source.

The published Windows package is self-contained, so end users do not need to install the .NET runtime.

## Install or run

### From a Windows package

1. Download and extract `MonthlyProfit-Windows-x64.zip` from the repository's pre-release builds, when one is available.
2. Run `MonthlyProfit-Setup.exe`.
3. The installer places the application under the current user's local application folder and attempts to create Desktop and Start menu shortcuts.

### From source

Clone the repository, then use the .NET CLI from the repository root:

```text
dotnet restore
dotnet run --project Desktop/Profit.Desktop.csproj
```

The application is a WPF app and can only run on Windows. The .NET CLI commands themselves are shell-independent; use a terminal appropriate to your platform.

## Configuration and data

Monthly Profit has no environment variables, service credentials, or external configuration files.

On Windows, the application stores its database at:

```text
%LOCALAPPDATA%\MonthlyProfit\Data\monthly-profit.sqlite
```

Backups are SQLite database files. Create a manual backup before restoring data or upgrading an existing installation. When automatic backups are enabled in the application, they run on exit; the default retention is eight recent copies.

## Typical workflow

1. Select or create a Persian calendar month.
2. Review and confirm the monthly rates for every active brand. Draft rates are based on the most recent confirmed month, or on the brand defaults when no earlier month exists.
3. Define or verify product-code prefixes for each brand.
4. Add sales manually or import a spreadsheet.
5. Record fixed expenses for the month and review the report.
6. Export the active month or a date range to Excel, print the active-month report, or create a backup.

Sales without a recognized brand, or without a confirmed monthly rate snapshot, are retained but excluded from calculated profit until the missing information is resolved.

### Sales import format

Imports accept `.xlsx` workbooks and SpreadsheetML `.xls` files up to 90 MB. The first worksheet is read. Header names are Persian and the importer recognizes these columns:

| Column | Purpose |
| --- | --- |
| `تاریخ` | Persian calendar date in `YYYYMMDD` format, for example `14050627` |
| `نام حساب` | Customer name |
| `کد کالا` | Product code used to identify the product and brand |
| `نام کالا` | Display name of the product |
| `تعداد واحد اصلی` or `تعداد` | Quantity sold |
| `قیمت` | Unit sale price |
| `قیمت کل` | Invoice total; calculated from quantity × unit price if omitted |
| `کسورات` | Optional invoice deductions |

Rows without a unit price, or with a unit price or total of 99 or less, are ignored. Valid rows are deduplicated using the imported file's content hash and row number.

## Calculation model

For a sale using the confirmed rate snapshot for its month:

```text
estimated gross purchase price per unit = unit sale price / (1 + markup)
estimated net cost per unit = estimated gross purchase price × (1 − purchase discount − offer)
estimated sale cost = estimated net cost per unit × quantity

net received = (invoice total − deductions) ×
               [credit share + cash share × (1 − cash discount)]

sale profit = net received − estimated sale cost
monthly result = sum of sale profit − fixed monthly expenses
```

The cash and credit shares must total 100%; all rate values must be between 0% and 100%; and the purchase discount plus offer cannot exceed 100%.

## Development

The solution is organized as follows:

| Path | Contents |
| --- | --- |
| `Core/` | Ledger models, calculations, SQLite persistence, spreadsheet import, and export |
| `Desktop/` | WPF application, dialogs, installer behavior, and bundled assets |
| `Tests/` | Console-based tests for business rules, persistence, and spreadsheet import/export |
| `.github/workflows/windows-build.yml` | Windows package build and pre-release workflow |

Run the test suite:

```text
dotnet run --project Tests/Profit.Tests.csproj --configuration Release
```

To create the self-contained Windows package, run the repository's packaging script in PowerShell on Windows:

```powershell
.\build-windows.ps1
```

The script runs the test project, publishes a self-contained `win-x64` single-file executable, creates `MonthlyProfit-Setup.exe`, calculates its SHA-256 checksum, and writes `out/MonthlyProfit-Windows-x64.zip`.

## CI and releases

The Windows build workflow runs on pushes to `main` and can also be started manually. After a successful build, it creates a GitHub pre-release containing the Windows ZIP package. It does not deploy a web service or publish to an app store.

## User guide

For the Persian end-user workflow, see [USER-GUIDE.fa.txt](USER-GUIDE.fa.txt).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the expected development and validation workflow.

## License

This repository does not currently declare a project license. Do not assume permission to reuse or redistribute the code beyond any rights separately granted by the copyright holder. The bundled Vazirmatn font is distributed under its included [SIL Open Font License](Desktop/Assets/Fonts/OFL.txt).
