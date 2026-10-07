# Cable Manufacturing Smoke

Run on Windows with .NET 8 Desktop installed:

```powershell
dotnet run --project validation/CableManufacturingSmoke -- "<fresh-output-directory>"
```

This does not show or operate application windows. It creates only synthetic CAD,
an empty central workbook, an isolated SQLite project, candidate archive assets,
WPF preview PNGs and a two-page draft PDF in the specified fresh directory.
Never pass a production data directory.

Assertions cover exact CAD contacts, exclusion of the old static CAD table by
named-block selection, independent physical cable instances, SQLite reload,
minimum table column widths, the actual shared WPF output renderer, PDF reopen,
failed-export cleanup, and unchanged project data after rendering.

The source CAD is intentionally marked synthetic. It is not manufacturer pinout
evidence or an engineering-approved cable. Normal mouse acceptance and testing
the owner's actual CAD remain owner UAT.
