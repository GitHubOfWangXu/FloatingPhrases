# Repository guidance

Floating Phrases is a Windows-only WPF application targeting .NET 8. Keep changes small and preserve the existing local-only, account-free design.

Before changing behavior, inspect the affected XAML, code-behind, storage class, and the self-test coverage. Preserve backward compatibility for JSON files under `%APPDATA%\FloatingPhrases`; add a migration and a regression check when a stored shape must change.

Validate changes with:

```powershell
dotnet build .\FloatingPhrases.csproj -c Release
dotnet run --project .\FloatingPhrases.SelfTest\FloatingPhrases.SelfTest.csproj -c Release
git diff --check
```

Use generic sample paths and data. Keep credentials, personal content, machine-specific absolute paths, generated output, and `%APPDATA%` data out of Git. Selection capture must stay within text exposed by Windows UI Automation and must not add OCR, permission bypasses, or whole-document collection.
