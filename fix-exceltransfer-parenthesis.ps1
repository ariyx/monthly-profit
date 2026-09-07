$ErrorActionPreference = 'Stop'
$file = Join-Path $PSScriptRoot 'Core\ExcelTransfer.cs'
$old = '                    }))));'
$new = '                    })))));'
$text = [System.IO.File]::ReadAllText($file)
if (-not $text.Contains($old)) { throw 'Expected ExcelTransfer.cs line was not found. Use the complete v0.1.2 package instead.' }
[System.IO.File]::WriteAllText($file, $text.Replace($old, $new), [System.Text.UTF8Encoding]::new($false))
Write-Host 'ExcelTransfer.cs fixed. Run git add .; git commit -m "Fix ExcelTransfer syntax"; git push'
