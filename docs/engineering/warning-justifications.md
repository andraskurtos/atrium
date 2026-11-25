# Atrium — Warning & Build Workaround Justification Registry

Every project-wide warning suppression or SDK build workaround in Atrium must be listed here alongside its inline `Justification:` comment in code. See [contributing-and-ci.md](./contributing-and-ci.md) for the full policy.

| ID / Setting | Location | Scope | Justification | Removal Criteria |
| :--- | :--- | :--- | :--- | :--- |
| `AllowMissingPrunePackageData=true` (`NETSDK1226`) | `api/Directory.Build.props` | `api/` solution | Arch/Linux distro `.NET 10.0.112` SDK packages omit `Microsoft.AspNetCore.App` prune metadata, causing `dotnet build`/`dotnet add package` to fail with `NETSDK1226`. Does not suppress code diagnostics. | Remove once Linux distro `.NET 10+` SDK packages ship complete AspNetCore prune metadata. |
| `CA1707` (`NoWarn`) | `api/Atrium.Api.Tests/Atrium.Api.Tests.csproj` | Test project only | xUnit test methods intentionally use `Unit_Outcome_WhenCondition` naming with underscores for readability. Production assemblies (`Atrium.Api`) still enforce `CA1707` as an error. | Permanent test-naming convention; revisit only if the repo switches to PascalCase-only test names. |
