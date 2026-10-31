# Test results

This document records the results of the automated tests (DR-05, NFR-22). The test cases and their mapping to the
requirements are in [Test_Cases.md](Test_Cases.md). The TRX files of each run are in [test-results/](test-results/).

## How the results were produced

```
dotnet build GSST.slnx            (Windows; GSST.Core.slnf on Linux)
dotnet test tests/Scheduler.UnitTests         --logger "trx;LogFileName=Scheduler.UnitTests.<machine>.trx"         --results-directory docs/test-results
dotnet test tests/Scheduler.ArchitectureTests --logger "trx;LogFileName=Scheduler.ArchitectureTests.<machine>.trx" --results-directory docs/test-results
```

The tests need no database and no environment variables: the use cases, the engine and the security module run on
an in-memory database. Every build must finish with 0 warnings and 0 errors.

## Run 1: Linux development container, 29 October 2026

| Item | Value |
|---|---|
| Machine | Ubuntu 24.04 container (Linux 6.18), .NET SDK 10.0.113 |
| Solution | `GSST.Core.slnf` (everything except the MAUI Admin app, which cannot be built on Linux) |
| Build | 0 warnings, 0 errors |

| Project | Total | Passed | Failed | Skipped | Duration | TRX file |
|---|---|---|---|---|---|---|
| Scheduler.UnitTests (includes 3 navigation tests and 37 tests on the in-memory database) | 132 | 132 | 0 | 0 | 25 s | `Scheduler.UnitTests.Linux.trx` |
| Scheduler.ArchitectureTests | 23 | 23 | 0 | 0 | 1 s | `Scheduler.ArchitectureTests.Linux.trx` |
| **Total** | **155** | **155** | **0** | **0** | | |

The suite was run twice in a row with the same result, to check that the timing-based engine tests are stable.

## Run 2: Windows laptop, 29 October 2026

| Item | Value |
|---|---|
| Machine | Windows laptop, .NET SDK 10.0.403 (10.0.300 also installed), workload set 10.0.400 with maui-windows 10.0.110, Visual Studio 18.11 |
| Solution | `GSST.slnx`, including Scheduler.Admin (net10.0-windows10.0.19041.0) |
| Build | 0 warnings, 0 errors |

| Project | Total | Passed | Failed | Skipped | Duration | TRX file |
|---|---|---|---|---|---|---|
| Scheduler.UnitTests | 132 | 132 | 0 | 0 | 20 s | `Scheduler.UnitTests.Windows.trx` |
| Scheduler.ArchitectureTests | 23 | 23 | 0 | 0 | 1 s | `Scheduler.ArchitectureTests.Windows.trx` |
| **Total** | **155** | **155** | **0** | **0** | | |

An earlier Windows run on the same day, before the tests moved to the in-memory database, also built all projects with
0 warnings and 0 errors.

## Manual and acceptance tests

The manual procedures (MC-01 to MC-22) and the click-through checklist in Test_Cases.md are carried out on the
Windows laptop before submission. MC-17 to MC-22 cover the behaviour that only PostgreSQL provides. The acceptance
tests are planned in [UAT_Plan.md](UAT_Plan.md) and reported in [UAT_Report.md](UAT_Report.md).
