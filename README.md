# Monthly Profit — Windows desktop implementation

Status: source implementation, not an accepted Windows release. No EXE has been built in this conversation.

C# / WPF / .NET 10, Microsoft.Data.Sqlite 10.0.8, offline single-user per-Windows-account storage. Persian RTL interface. Amounts use `decimal`; rates are fractional decimals in storage. Monthly product snapshots prevent later edits from rewriting history. Optimistic revisions and SQLite transactions protect saves. Backups use SQLite's online backup API and are integrity-checked before restoration; pre-restore copies are retained.

The actual input workbook remains the source of the six-row reference dataset. Item names in sample/imported legacy data are explicitly placeholders, since the original workbook identifies only brands.

## Projects

- Core: business rules, local database, bounded OOXML input/output (no macro execution).
- Desktop: dashboard, monthly files, brand/product editor, monthly comparison, printing, Excel transfer, backup/restore and per-user first-run installer.
- Tests: executable acceptance suite for financial outcomes, zero-sales handling, persistence, backup, month independence and spreadsheet roundtrip.
- build-windows.ps1: runs tests and packages the self-contained installer. For the build operator, not the end user.
- .github/workflows/windows-build.yml: Windows hosted build; not run or uploaded automatically in this conversation.

## Build operator

Run `./build-windows.ps1` with .NET 10 SDK on a trusted Windows x64 build host. The output package includes a self-contained EXE; the user does not need an SDK or installed .NET. The EXE named `MonthlyProfit-Setup.exe` offers per-user installation and creates shortcuts, copying itself to `MonthlyProfit.exe`. It does not modify the separate data directory. The resulting executable is unsigned until a release operator signs it.

The current sandbox could download and verify SDK 10.0.400 SHA512, but could not initialize CoreCLR (`HRESULT 0x8007000E`). Accordingly no C# build/test execution or WPF visual acceptance is claimed. Static project/XML and independent reference calculations are the only locally runnable verification until a build host is available.

## Required Windows acceptance gate

1. Build and run the included C# tests; no failures or compile errors.
2. Install on clean Windows 11 x64 and the target Windows 10 edition/build without preinstalled .NET or Excel, then launch offline as a standard user. .NET 10 official OS support varies by Windows edition/lifecycle; do not declare blanket Windows 10 support.
3. Test RTL UI at 100%, 125%, 150% scaling; modal editor, amount formatting, small window, keyboard navigation and print output.
4. Create two months and two products in the same brand with different prices/rates. Save, close and reopen; verify persistence and historical independence.
5. Validate blank/text/negative input, share totals, zero sales, losses, fractional quantity and duplicate products.
6. Import the original workbook, reconcile cost 18,070,000; sales 26,634,400; profit 8,564,400; net -43,435,600 rials. Double shampoo quantity from 2 to 4: price/unit stays 5,200,000; sales and product profit double.
7. Backup to external folder, edit data, restore, verify safety copy and all months. Attempt corrupt/incompatible backups; active data must remain intact.
8. Reinstall over an existing version with the app closed; check data survival and shortcuts. Test installer when app is running; require closing the app.
9. Open Excel export in Excel/LibreOffice and reimport it. Check precision, percent formatting and absence of active formulas or external links.

## Sources consulted

- https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/
- https://learn.microsoft.com/en-us/windows/apps/develop/data-access/sqlite-data-access
- https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md
