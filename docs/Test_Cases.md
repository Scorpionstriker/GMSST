# Test cases

This document lists the test cases for every requirement in the Requirements Specification (FR-01 to FR-79, NFR-01
to NFR-22, BR-01 to BR-12 and DR-01 to DR-07), as DR-05 asks. Each automated test case names the test in the
`tests/` projects; each manual case refers to a procedure in section 3. Results are summarised in
[Test_Results.md](Test_Results.md).

| Method | Where | How to run |
|---|---|---|
| Unit | `tests/Scheduler.UnitTests`; tests named `InMemory…` run the use cases, the engine and the security module on an in-memory database | `dotnet test tests/Scheduler.UnitTests` (no PostgreSQL needed) |
| Architecture | `tests/Scheduler.ArchitectureTests` | `dotnet test tests/Scheduler.ArchitectureTests` |
| Navigation | `tests/Scheduler.UnitTests/NavigationTests.cs` | Part of the unit tests |
| Manual | Section 3 of this document | By hand in the Admin app |
| UAT | [UAT_Plan.md](UAT_Plan.md) | With participants |

All automated tests run without PostgreSQL. Behaviour that only PostgreSQL provides (claiming with SKIP LOCKED
across connections, the single-engine advisory lock, constraints, migrations, pg_dump backups and bulk retention)
is checked by the manual procedures MC-17 to MC-22. The Result column shows the latest run: "Pass" for automated
tests that passed, and "Not run yet" for manual and UAT cases that are carried out before submission.

## 1. Requirements covered

| ID | Requirement (short form) |
|---|---|
| FR-01 | Create a task |
| FR-02 | Edit, pause, resume, run now and delete a task |
| FR-03 | Task states Active, Paused, Disabled |
| FR-04 | Soft delete |
| FR-05 | Unique task names ignoring case |
| FR-06 | Task types match registered handlers |
| FR-07 | Four built-in task types, no shell type |
| FR-08 | Created by and modified by |
| FR-09 | Five-field cron |
| FR-10 | Interval schedules |
| FR-11 | Once schedules |
| FR-12 | Start and end window |
| FR-13 | Next run time stored and recalculated |
| FR-14 | IANA time zones, UTC storage |
| FR-15 | Misfire policies |
| FR-16 | Preview of the next five runs |
| FR-17 | More than one schedule over a task's life |
| FR-18 | Adaptive polling |
| FR-19 | Queue with priority, time and trigger type |
| FR-20 | Claim with SKIP LOCKED and lease, priority order |
| FR-21 | Bounded worker pool (MaxConcurrency) |
| FR-22 | Handler execution under a time-out |
| FR-23 | Execution statuses |
| FR-24 | Cancel a waiting queue entry |
| FR-25 | One engine per database; only registered task types queued |
| FR-26 | Dependencies: all prerequisites succeeded |
| FR-27 | No self, circular or duplicate dependencies |
| FR-28 | Logging of all activity with levels |
| FR-29 | Console, File and Database providers; module providers |
| FR-30 | Errors with type, message and stack trace |
| FR-31 | Retries with exponential back-off |
| FR-32 | Execution isolation |
| FR-33 | Recovery at start-up |
| FR-34 | Controlled shutdown |
| FR-35 | Retention of queue, history, logs, log files and audit |
| FR-36 | Global and per-task settings |
| FR-37 | Setting validation |
| FR-38 | Migrations and seed data at first start |
| FR-39 | Configuration cache |
| FR-40 | Public extensibility contracts |
| FR-41 | Modules register task types, providers, pages and menus |
| FR-42 | Two integration methods |
| FR-43 | Scheduler events |
| FR-44 | Removable Login and Security module, login bypass |
| FR-45 | Sign-in required, sign out |
| FR-46 | PBKDF2 password hashing |
| FR-47 | Lockout with a generic message |
| FR-48 | Session time-out |
| FR-49 | Administrator and Viewer roles |
| FR-50 | First administrator at first start |
| FR-51 | User management, own password change, audited |
| FR-52 | Password policy |
| FR-53 | Login audit |
| FR-54 | Persistent menu |
| FR-55 | Every button, menu item and link works |
| FR-56 | Dashboard figures |
| FR-57 | Engine status and user in the header |
| FR-58 | Task list search and execution details |
| FR-59 | Error log and queue pages |
| FR-60 | Modules and Help pages |
| FR-61 | Confirmation naming the object |
| FR-62 | Notifications, field messages, error banners |
| FR-63 | Coloured labels with text; light and dark theme |
| FR-64 | Five reports |
| FR-65 | Report filters (date, task, status) |
| FR-66 | CSV export and printing |
| FR-67 | Backup on demand, chosen databases and folder |
| FR-68 | pg_dump custom format sets named by date and time |
| FR-69 | Daily backup at 01:00 local time |
| FR-70 | Verification, manifest with SHA-256, copy folder |
| FR-71 | Backup retention |
| FR-72 | Restore after typing RESTORE |
| FR-73 | Safety backup before migrations |
| FR-74 | Validation in the UI and the application layer |
| FR-75 | Task editor validation rules |
| FR-76 | Constrained controls |
| FR-77 | Separate, independently maintainable modules |
| FR-78 | Inventory Monitor module with its own database |
| FR-79 | Maintenance Agent as a separate managed application |
| NFR-01 | Reliability |
| NFR-02 | Running system |
| NFR-03 | Concurrency safety |
| NFR-04 | Usability |
| NFR-05 | User friendliness |
| NFR-06 | Maintainability |
| NFR-07 | Extensibility |
| NFR-08 | Flexibility |
| NFR-09 | Modularity |
| NFR-10 | Cross-platform compatibility |
| NFR-11 | Performance and resource efficiency |
| NFR-12 | Scalability |
| NFR-13 | Security and dependency control |
| NFR-14 | Local hosting and developer control |
| NFR-15 | Protection of secrets |
| NFR-16 | Injection prevention |
| NFR-17 | Data integrity |
| NFR-18 | Auditability |
| NFR-19 | Recoverability |
| NFR-20 | Transparency |
| NFR-21 | Documentation |
| NFR-22 | Testability |
| BR-01 | Every table shall have a universally unique identifier (UUID) primary key generated by the… |
| BR-02 | All timestamps shall be stored as TIMESTAMPTZ in Coordinated Universal Time |
| BR-03 | Each schedule shall belong to exactly one task |
| BR-04 | Each execution shall reference a valid task, queue entry and worker; the schedule is optio… |
| BR-05 | A task shall not depend on itself, and circular dependency chains shall be rejected |
| BR-06 | A cron expression shall be a valid five-field expression when the schedule type is Cron, a… |
| BR-07 | Task, queue, execution, worker, log level, provider, scope and role values shall be restri… |
| BR-08 | The retry count shall not be negative and shall not exceed the applicable retry limit |
| BR-09 | Priority shall be a whole number from 1 to 10, with 5 as the default and 1 claimed first |
| BR-10 | Each configuration key shall have one value per scope; a setting is Global exactly when it… |
| BR-11 | Usernames and task names shall be unique, ignoring letter case |
| BR-12 | Referential integrity shall be enforced by foreign keys, so that no schedule, queue entry… |
| DR-01 | A fully functional, locally hosted, cross-platform modular system scheduler with its Admin… |
| DR-02 | Source code, database migrations and documentation versioned together in a GitHub reposito… |
| DR-03 | Technical documentation describing the architecture, module interactions, configuration pr… |
| DR-04 | A user manual, including an installation and configuration guide |
| DR-05 | Sample tasks, test data, test cases and test results from unit, integration and system tes… |
| DR-06 | A User Acceptance Testing plan and report evaluating usability, reliability and effectiven… |
| DR-07 | A final academic project report, presentation and demonstration of the running system |
## 2. Test cases

| Requirement ID | Test ID | Method | Steps or test name | Expected result | Result |
|---|---|---|---|---|---|
| FR-01 | TC-001 | Unit | InMemoryUseCaseTests.CreateTask_InALocalTimeZone_StoresUtcAndTheNextRun | The task is stored with its fields and next run | Pass |
| FR-01 | TC-002 | Manual | MC-03 | Saved task appears in the list | Not run yet |
| FR-02 | TC-003 | Unit | InMemoryEngineTests.RunNow_ExecutesTheTaskOnce_AndRecordsSuccess | One Manual run, status unchanged | Pass |
| FR-02 | TC-004 | Unit | InMemorySchedulingTests.PausedTask_IsNotRunByItsSchedule | Paused task not run | Pass |
| FR-02 | TC-005 | Manual | MC-04, MC-05 | Edit, pause, resume and delete work from the list and editor | Not run yet |
| FR-03 | TC-006 | Unit | TaskStateTests (3 tests) | Only allowed transitions; Paused runs only manually; Disabled never runs | Pass |
| FR-03 | TC-007 | Unit | InMemorySchedulingTests.ManualRunOfAPausedTask_KeepsItsRetries_AndADisabledTaskCannotRun | As the state table | Pass |
| FR-04 | TC-008 | Unit | InMemorySchedulingTests.OlderTaskWithTheSameNameAndType_IsTakenOverByItsKey_AndRestored | A deleted task can be restored | Pass |
| FR-04 | TC-009 | Manual | MC-05 | Restore brings the task back | Not run yet |
| FR-05 | TC-010 | Unit | InMemoryUseCaseTests.TaskNames_AreUniqueIgnoringCase | Duplicate name in another case refused | Pass |
| FR-05 | TC-011 | Manual | MC-18 | The unique index refuses a duplicate in the database | Not run yet |
| FR-06 | TC-012 | Unit | ApplicationWiringTests.TwoHandlersForTheSameTaskType_FailWithAClearMessage | Duplicate type names refused | Pass |
| FR-06 | TC-013 | Unit | InMemorySchedulingTests.DueTaskOfAnUnregisteredType_IsNotQueued | Unregistered type not queued | Pass |
| FR-07 | TC-014 | Unit | HandlerTests.LogEntry_SucceedsForAValidPayload, HandlerTests.LocalHttp_OnlyLoopbackIsAllowed | Built-in types work; no shell type exists | Pass |
| FR-07 | TC-015 | Manual | MC-19, MC-20 | Backup and clean-up tasks work | Not run yet |
| FR-08 | TC-016 | Manual | MC-03, then SELECT created_by, updated_by FROM scheduler.task | Both columns hold the signed-in user and the time | Not run yet |
| FR-09 | TC-017 | Unit | CronExpressionTests (12 tests) | Valid expressions give the right next time; invalid ones a clear message | Pass |
| FR-10 | TC-018 | Unit | ScheduleCalculatorTests.Interval_NextRunIsOneIntervalAfterTheLastDueTime | Next run one interval later | Pass |
| FR-11 | TC-019 | Unit | ScheduleCalculatorTests.Once_IsDueUntilItHasRun | Runs once only | Pass |
| FR-12 | TC-020 | Unit | ScheduleCalculatorTests.EndTime_StopsTheSchedule | No runs after the end time | Pass |
| FR-12 | TC-021 | Manual | MC-03 | Once needs a future time; cron start may be empty | Not run yet |
| FR-13 | TC-022 | Unit | ScheduleCalculatorTests.OnTimeRun_IsQueuedOnceAsScheduled | Next run recalculated after each run | Pass |
| FR-14 | TC-023 | Unit | CronExpressionTests.NextOccurrence_InANonUtcZone_IsConvertedToUtc, NextOccurrence_FollowsSummerTime | Local time converted to UTC | Pass |
| FR-15 | TC-024 | Unit | ScheduleCalculatorTests.Misfire_Skip_QueuesNothing, Misfire_RunOnce_QueuesOneCatchUpRun, Misfire_RunAll_QueuesEveryMissedRunUpToTheCap | Each policy behaves as specified | Pass |
| FR-15 | TC-025 | Unit | InMemorySchedulingTests.MissedRuns_FollowTheMisfirePolicy | Same, end to end | Pass |
| FR-16 | TC-026 | Unit | ScheduleCalculatorTests.Preview_ReturnsTheNextFiveRuns; ApplicationWiringTests.Preview_ReturnsFutureRunsForAValidSchedule | Five future times | Pass |
| FR-17 | TC-027 | Manual | MC-04 | Changing the schedule type keeps history; the new schedule is used | Not run yet |
| FR-18 | TC-028 | Unit | InMemorySchedulingTests.Engine_DoesNotSpinWhileTheOnlyWorkerIsBusy | No busy loop | Pass |
| FR-18 | TC-029 | Unit | EngineSignalTests (3 tests) | Wake signal ends the sleep early | Pass |
| FR-19 | TC-030 | Unit | InMemoryEngineTests.RunNow_ExecutesTheTaskOnce_AndRecordsSuccess, Claim_TakesTheHighestPriorityFirst_ThenTheEntryQueuedFirst | Entries carry priority, time and trigger | Pass |
| FR-20 | TC-031 | Unit | InMemoryEngineTests.ParallelClaims_TakeEveryEntryExactlyOnce | Each entry claimed once | Pass |
| FR-20 | TC-032 | Unit | InMemoryEngineTests.Claim_TakesTheHighestPriorityFirst_ThenTheEntryQueuedFirst | Priority, then time, then queued time | Pass |
| FR-20 | TC-033 | Manual | MC-17 | SKIP LOCKED claiming on PostgreSQL during a busy run | Not run yet |
| FR-21 | TC-034 | Unit | InMemorySchedulingTests.Engine_DoesNotSpinWhileTheOnlyWorkerIsBusy | Never more than MaxConcurrency running | Pass |
| FR-22 | TC-035 | Unit | InMemoryDeliveryTests.HandlerThatIgnoresItsTimeOut_IsAbandonedAndRecordedAsTimedOut | TimedOut recorded | Pass |
| FR-23 | TC-036 | Unit | InMemoryEngineTests (6 tests), InMemoryDeliveryTests (4 tests) | Statuses recorded as runs progress | Pass |
| FR-23 | TC-037 | Manual | MC-18 | Check constraints accept only the six statuses | Not run yet |
| FR-24 | TC-038 | Manual | MC-07 | Cancel on the Queue page asks for confirmation and cancels the entry | Not run yet |
| FR-25 | TC-039 | Manual | MC-17 | Second Admin app does not start its engine and names the running host | Not run yet |
| FR-25 | TC-040 | Unit | InMemorySchedulingTests.DueTaskOfAnUnregisteredType_IsNotQueued | Not queued | Pass |
| FR-25 | TC-041 | Unit | InMemorySchedulingTests.StoppedWorkerOfThisHost_IsRevived | Worker revived | Pass |
| FR-25 | TC-042 | Manual | MC-03 | "No handler" badge shown | Not run yet |
| FR-26 | TC-043 | Unit | InMemorySchedulingTests.Dependant_RunsOnlyAfterAllPrerequisitesSucceeded_OncePerRound | Queued once after all succeed | Pass |
| FR-26 | TC-044 | Unit | InMemorySchedulingTests.FinalFailureOfAPrerequisite_StopsTheRound | Not queued after a final failure | Pass |
| FR-26 | TC-045 | Unit | InMemoryEngineTests.SuccessfulTask_QueuesItsDependant | Dependant queued | Pass |
| FR-27 | TC-046 | Unit | DependencyGraphTests.DirectAndIndirectCycles_AreDetected | Cycles found | Pass |
| FR-27 | TC-047 | Unit | InMemoryUseCaseTests.Dependencies_ThatWouldFormACycle_AreRejected | Refused with a message | Pass |
| FR-28 | TC-048 | Unit | InMemoryEngineTests.RunNow_ExecutesTheTaskOnce_AndRecordsSuccess | Run recorded | Pass |
| FR-28 | TC-049 | Manual | MC-08 | Info entries shown for the run | Not run yet |
| FR-29 | TC-050 | Manual | MC-10 | Built-in and module providers listed; can be switched and levels set | Not run yet |
| FR-30 | TC-051 | Unit | InMemoryEngineTests.ThrowingHandler_IsRecordedWithItsStackTrace | Type, message and stack trace stored | Pass |
| FR-31 | TC-052 | Unit | RetryPolicyTests.Delay_DoublesUpToTheMaximum, Retries_StopAtTheLimit_AndForInactiveTasks | Back-off and limit | Pass |
| FR-31 | TC-053 | Unit | InMemoryEngineTests.FailedRun_IsRetried_AndFinallyMarkedFailed | Retried then Failed | Pass |
| FR-32 | TC-054 | Unit | InMemoryEngineTests.ThrowingHandler_IsRecordedWithItsStackTrace, InMemoryDeliveryTests.HandlerThatIgnoresItsTimeOut_IsAbandonedAndRecordedAsTimedOut | Engine keeps running | Pass |
| FR-33 | TC-055 | Unit | InMemorySchedulingTests.Recovery_RetriesRunsInterruptedByACrash_AndReleasesUnstartedClaims | Interrupted runs retried, unstarted claims released | Pass |
| FR-33 | TC-056 | Unit | InMemoryDeliveryTests.CrashBeforeTheClaim_TheWaitingEntryRunsAfterRestart | Waiting entry runs after restart | Pass |
| FR-34 | TC-057 | Unit | InMemorySchedulingTests.Stop_CancelsRunningTasks_AndQueuesThemAgain | Cancelled and queued again | Pass |
| FR-34 | TC-058 | Manual | MC-12 | Closing the window during a run exits within the grace period | Not run yet |
| FR-35 | TC-059 | Unit | LogFileCleanerTests.OldLogFiles_AreDeleted_OnlyWhenTheyMatchTheFileProviderPattern | Only old log files deleted | Pass |
| FR-35 | TC-060 | Manual | MC-20 | Old queue entries, history and audit removed; executions kept | Not run yet |
| FR-36 | TC-061 | Unit | InMemoryUseCaseTests.Settings_AreValidatedBeforeTheyAreStored | Global values stored | Pass |
| FR-36 | TC-062 | Manual | MC-10 | Task override used by that task | Not run yet |
| FR-37 | TC-063 | Unit | InMemoryUseCaseTests.Settings_AreValidatedBeforeTheyAreStored | Out-of-range value refused | Pass |
| FR-38 | TC-064 | Manual | MC-18 | Tables and defaults created once on an empty database | Not run yet |
| FR-39 | TC-065 | Unit | InMemoryUseCaseTests.Settings_AreValidatedBeforeTheyAreStored | New value used at once | Pass |
| FR-39 | TC-066 | Manual | MC-10 | Change takes effect without a restart | Not run yet |
| FR-40 | TC-067 | Architecture | LayerDependencyTests.Project_DoesNotDependOnForbiddenLayers | Modules depend only on Extensibility | Pass |
| FR-41 | TC-068 | Navigation | NavigationTests.ModuleMenuEntries_PointToPagesOfThatModule | Module pages and menus found | Pass |
| FR-41 | TC-069 | Manual | MC-11 | Modules page lists types and pages | Not run yet |
| FR-42 | TC-070 | Unit | HandlerTests.Connector_TaskPayloadKeepsTheKeyOutOfTheDatabase | Agent method works | Pass |
| FR-42 | TC-071 | Manual | MC-14, MC-15 | Both demo apps work end to end | Not run yet |
| FR-43 | TC-072 | Unit | InMemoryEngineTests.RunNow_ExecutesTheTaskOnce_AndRecordsSuccess | TaskSucceeded event received | Pass |
| FR-43 | TC-073 | Manual | MC-01 | Dashboard updates without Refresh | Not run yet |
| FR-44 | TC-074 | Unit | LoginBypassTests (5 tests) | Required, blocked, or bypassed with a banner as specified | Pass |
| FR-45 | TC-075 | Unit | InMemorySecurityTests.FirstAdministrator_CanBeCreatedOnlyOnce_AndThenSignIn | Sign-in works | Pass |
| FR-45 | TC-076 | Manual | MC-16 | Pages need sign-in; Sign out returns to login | Not run yet |
| FR-46 | TC-077 | Unit | PasswordHasherTests (6 tests) | Salted PBKDF2, no plain text | Pass |
| FR-47 | TC-078 | Unit | InMemorySecurityTests.FiveWrongPasswords_LockTheAccount_WithAGenericMessage, WrongRecoveryCodes_LockTheAccount_WithOneGenericMessage | Locked after five wrong passwords or recovery codes, same message | Pass |
| FR-48 | TC-079 | Manual | MC-16 | Signed out after the time-out | Not run yet |
| FR-49 | TC-080 | Unit | ApplicationWiringTests.AdministratorCommands_AreRefusedForAViewer | Viewer refused | Pass |
| FR-49 | TC-081 | Unit | InMemoryUseCaseTests.ViewerRole_CannotChangeData | Viewer refused | Pass |
| FR-49 | TC-082 | Manual | MC-16 | Admin-only menu items hidden from a Viewer | Not run yet |
| FR-50 | TC-083 | Unit | InMemorySecurityTests.FirstAdministrator_CanBeCreatedOnlyOnce_AndThenSignIn, Register_IsRefused_BeforeTheFirstAdministratorExists | Only once; registering waits for it | Pass |
| FR-51 | TC-084 | Unit | InMemorySecurityTests.UserChanges_AreAudited_WithoutPasswords, ViewerSession_CannotManageUsers_ButCanChangeOwnPassword, LastAdministrator_CannotBeDeactivated, RegisteredAccount_IsAnInactiveViewer_UntilAnAdministratorActivatesIt, ForgotPassword_WithTheRecoveryCode_SetsANewPassword_AndTheCodeWorksOnce, NewRecoveryCode_ReplacesTheOldOne_AfterThePasswordIsEntered | Managed and audited; registered accounts wait for activation; a forgotten password is reset once with the recovery code | Pass |
| FR-52 | TC-085 | Unit | PasswordHasherTests.PasswordPolicy_AcceptsOnlyStrongPasswords | Weak passwords refused | Pass |
| FR-53 | TC-086 | Unit | InMemorySecurityTests.FiveWrongPasswords_LockTheAccount_WithAGenericMessage | Each attempt audited with its reason | Pass |
| FR-54 | TC-087 | Navigation | NavigationTests.EveryMenuEntry_HasAPage | Every menu entry opens a page | Pass |
| FR-55 | TC-088 | Navigation | NavigationTests (3 tests) | Every link has a page and every page is reachable | Pass |
| FR-55 | TC-089 | Manual | Manual click-through checklist | Every button does what its label says | Not run yet |
| FR-56 | TC-090 | Manual | MC-01 | Figures match the data | Not run yet |
| FR-57 | TC-091 | Manual | MC-01 | Header shows engine status and user | Not run yet |
| FR-58 | TC-092 | Manual | MC-08 | Search and filters work; selecting a run shows its details | Not run yet |
| FR-59 | TC-093 | Manual | MC-07, MC-09 | Queue and Error log pages filter and show stack traces | Not run yet |
| FR-60 | TC-094 | Manual | MC-11 | Modules and Help pages complete | Not run yet |
| FR-61 | TC-095 | Manual | Manual click-through checklist | Each dialog names the object | Not run yet |
| FR-62 | TC-096 | Unit | InMemoryUseCaseTests.CreateTask_ReportsEveryInvalidField | Per-field messages | Pass |
| FR-62 | TC-097 | Manual | MC-03 | Toasts, field messages and banners shown | Not run yet |
| FR-63 | TC-098 | Manual | MC-02 | Labels have text; theme switch works | Not run yet |
| FR-64 | TC-099 | Manual | MC-13 | All five reports render, including the Security and audit report | Not run yet |
| FR-65 | TC-100 | Manual | MC-13 | Date, task and status filters change the results; status hidden for Upcoming schedule | Not run yet |
| FR-66 | TC-101 | Unit | CsvWriterTests.Values_AreQuotedWhenNeeded, FormulaLikeValues_CannotRunInASpreadsheet | Valid, safe CSV | Pass |
| FR-66 | TC-102 | Manual | MC-13 | Export CSV and Print work | Not run yet |
| FR-67 | TC-103 | Manual | MC-19 | Chosen databases backed up to the chosen folder | Not run yet |
| FR-68 | TC-104 | Manual | MC-19 | One custom-format file per database, set named by date and time | Not run yet |
| FR-69 | TC-105 | Unit | InMemoryUseCaseTests.BuiltInTask_SurvivesARename_RunsByKey_AndCannotBeDeleted | Built-in tasks are kept by key | Pass |
| FR-69 | TC-106 | Unit | CronExpressionTests.LocalTimeSkippedByDaylightSaving_RunsAtTheFirstValidMinuteAfterTheGap | Skipped time runs at the next minute | Pass |
| FR-69 | TC-107 | Manual | MC-19 | Database Backup task at 01:00 backs up all three databases | Not run yet |
| FR-70 | TC-108 | Manual | MC-19 | Verified, checksummed and copied to the copy folder | Not run yet |
| FR-71 | TC-109 | Manual | MC-19 | Older sets removed after BackupRetentionCount | Not run yet |
| FR-72 | TC-110 | Manual | MC-12, MC-19 | Restore needs RESTORE; damaged set refused; agent check; sign-out after gmss | Not run yet |
| FR-73 | TC-111 | Manual | MC-21 | Migrations held back when the safety backup fails | Not run yet |
| FR-74 | TC-112 | Unit | ApplicationWiringTests.Validators_AreRegisteredAndRun | Server-side validation runs | Pass |
| FR-74 | TC-113 | Manual | MC-03 | Fields checked on leaving them | Not run yet |
| FR-75 | TC-114 | Unit | InMemoryUseCaseTests.CreateTask_ReportsEveryInvalidField | Every rule reported | Pass |
| FR-76 | TC-115 | Manual | MC-03 | Drop-downs for type, zone, policy and role | Not run yet |
| FR-77 | TC-116 | Architecture | LayerDependencyTests (3 tests), AssemblyLoading.AllCheckedAssembliesAreReferenced | Layer rules hold | Pass |
| FR-78 | TC-117 | Manual | MC-14 | Inventory pages work; data in inventory_demo; Viewer cannot edit | Not run yet |
| FR-79 | TC-118 | Unit | HandlerTests.AgentKey_IsRequiredAndCompared, LocalHttp_ReadsEnvHeaderValuesWhenTheTaskRuns, LocalHttp_UsesTheNotRunningMessageWhenNothingListens, TempFileCleanup_OnlyWorksInsideAllowedFolders | Key required, loopback only, clear message | Pass |
| FR-79 | TC-119 | Manual | MC-15 | Agent page and tasks work | Not run yet |
| NFR-01 | TC-120 | Unit | InMemoryDeliveryTests (4 tests) | Starts within 5 s; work not lost; may run again | Pass |
| NFR-01 | TC-121 | Unit | InMemorySchedulingTests.Recovery_RetriesRunsInterruptedByACrash_AndReleasesUnstartedClaims | Recovered | Pass |
| NFR-02 | TC-122 | UAT | UAT scenarios S1 to S10 | No crash during UAT | Not run yet |
| NFR-03 | TC-123 | Unit | InMemoryEngineTests.ParallelClaims_TakeEveryEntryExactlyOnce | No entry claimed twice | Pass |
| NFR-03 | TC-124 | Manual | MC-17 | One engine per database on PostgreSQL | Not run yet |
| NFR-04 | TC-125 | UAT | UAT; Installation guide timed | Installed in under 30 minutes by following the guide | Not run yet |
| NFR-05 | TC-126 | UAT | UAT questionnaire | SUS at least 68; each scenario under 5 minutes | Not run yet |
| NFR-06 | TC-127 | Architecture | LayerDependencyTests (3 tests) | Layers stay separate | Pass |
| NFR-07 | TC-128 | Architecture | LayerDependencyTests.Project_DoesNotDependOnForbiddenLayers | Demo modules use only Extensibility | Pass |
| NFR-08 | TC-129 | Unit | InMemoryUseCaseTests.Settings_AreValidatedBeforeTheyAreStored | Behaviour changed by settings without code changes | Pass |
| NFR-09 | TC-130 | Architecture | LayerDependencyTests (3 tests) | Modules removable; rules enforced | Pass |
| NFR-10 | TC-131 | Manual | Build on Windows (Test_Results.md); macOS and Linux guides | Builds on Windows; core on Linux | Not run yet |
| NFR-11 | TC-132 | Unit | InMemorySchedulingTests.Engine_DoesNotSpinWhileTheOnlyWorkerIsBusy | No busy loop | Pass |
| NFR-11 | TC-133 | Manual | MC-01 | Idle CPU under 2 per cent in Task Manager; dashboard updates within 5 s | Not run yet |
| NFR-12 | TC-134 | Manual | MC-08 | Lists stay paged and responsive with many runs | Not run yet |
| NFR-13 | TC-135 | Manual | Directory.Packages.props, global.json | Every package and the SDK pinned | Not run yet |
| NFR-14 | TC-136 | Unit | HandlerTests.LocalHttp_OnlyLoopbackIsAllowed | Local only | Pass |
| NFR-14 | TC-137 | Manual | Installation_Guide.md 2.1 | PostgreSQL local-only settings applied | Not run yet |
| NFR-15 | TC-138 | Unit | HandlerTests.Connector_TaskPayloadKeepsTheKeyOutOfTheDatabase | Key not stored | Pass |
| NFR-15 | TC-139 | Manual | Code review: GMSS_DB_PASSWORD only | No password in any file | Not run yet |
| NFR-16 | TC-140 | Unit | CsvWriterTests.FormulaLikeValues_CannotRunInASpreadsheet | CSV injection neutralised | Pass |
| NFR-16 | TC-141 | Manual | MC-18 | Constraints refuse invalid values; code review: parameterised SQL only | Not run yet |
| NFR-17 | TC-142 | Unit | InMemoryUseCaseTests.UpdateTask_WithAnOldVersion_IsAConflict, InMemorySecurityTests.EditingAUserThatChangedSinceItWasOpened_IsRefused | Stale edits refused | Pass |
| NFR-17 | TC-143 | Manual | MC-22 | Stale edit refused on PostgreSQL | Not run yet |
| NFR-18 | TC-144 | Unit | InMemorySecurityTests.UserChanges_AreAudited_WithoutPasswords | Audited with the administrator | Pass |
| NFR-19 | TC-145 | Manual | MC-19; Installation_Guide.md section 7 | Restored on another device within 15 minutes | Not run yet |
| NFR-20 | TC-146 | Manual | MC-08, MC-09 | Every run and error visible with details | Not run yet |
| NFR-21 | TC-147 | Manual | docs/ folder review | All guides present and correct | Not run yet |
| NFR-22 | TC-148 | Manual | Test_Results.md | All automated tests pass with 0 warnings | Not run yet |
| BR-01 | TC-149 | Manual | MC-18 | UUID keys in every table | Not run yet |
| BR-02 | TC-150 | Unit | CronExpressionTests.NextOccurrence_InANonUtcZone_IsConvertedToUtc | UTC stored | Pass |
| BR-02 | TC-151 | Unit | InMemoryUseCaseTests.CreateTask_InALocalTimeZone_StoresUtcAndTheNextRun | Stored in UTC | Pass |
| BR-03 | TC-152 | Manual | MC-18 | Schedule needs its task | Not run yet |
| BR-04 | TC-153 | Manual | MC-20 | Queue reference cleared, execution kept | Not run yet |
| BR-05 | TC-154 | Unit | DependencyGraphTests.DirectAndIndirectCycles_AreDetected | Refused | Pass |
| BR-06 | TC-155 | Unit | CronExpressionTests.Invalid_ExpressionsAreRejectedWithAClearMessage | Refused | Pass |
| BR-06 | TC-156 | Unit | InMemoryUseCaseTests.CreateTask_ReportsEveryInvalidField | Invalid cron refused by the validator | Pass |
| BR-07 | TC-157 | Manual | MC-18 | Check constraints refuse other values | Not run yet |
| BR-08 | TC-158 | Unit | RetryPolicyTests.Retries_StopAtTheLimit_AndForInactiveTasks | Never above the limit | Pass |
| BR-09 | TC-159 | Unit | InMemoryEngineTests.Claim_TakesTheHighestPriorityFirst_ThenTheEntryQueuedFirst | 1 claimed first | Pass |
| BR-09 | TC-160 | Unit | InMemoryUseCaseTests.CreateTask_ReportsEveryInvalidField | Range 1 to 10 | Pass |
| BR-10 | TC-161 | Unit | InMemoryUseCaseTests.Settings_AreValidatedBeforeTheyAreStored | One value per scope | Pass |
| BR-11 | TC-162 | Unit | InMemoryUseCaseTests.TaskNames_AreUniqueIgnoringCase | Case-insensitive unique | Pass |
| BR-11 | TC-163 | Manual | MC-18 | Unique indexes in the database | Not run yet |
| BR-12 | TC-164 | Manual | MC-18 | Foreign keys enforced (except the BR-04 queue reference) | Not run yet |
| DR-01 | TC-165 | Manual | Demonstration path MC-01 to MC-16 | Whole system runs on the laptop | Not run yet |
| DR-02 | TC-166 | Manual | Repository review | Code, migrations and docs together | Not run yet |
| DR-03 | TC-167 | Manual | Technical_Guide.md | Present and current | Not run yet |
| DR-04 | TC-168 | Manual | User_Manual.md, Installation_Guide.md, Build_macOS.md, Build_Linux.md | Present and current | Not run yet |
| DR-05 | TC-169 | Manual | Test_Cases.md, Test_Results.md, docs/test-results/ | Present | Not run yet |
| DR-06 | TC-170 | Manual | UAT_Plan.md, UAT_Report.md | Present; report filled in after the sessions | Not run yet |
| DR-07 | TC-171 | Manual | Outside the code | Project report, presentation and demonstration | Outside the code |

## 3. Manual procedures

Run these on the Windows laptop with the Admin app, signed in as an administrator unless stated otherwise, and with
the Maintenance Agent running.

| ID | Procedure | Expected result | Result |
|---|---|---|---|
| MC-01 | Open the Dashboard. Run a task with Run now and watch the page without pressing Refresh. Check Task Manager while nothing runs | The header shows the engine status and your username; figures update within 5 seconds; idle CPU under 2 per cent | Not run yet |
| MC-02 | Switch between the light and dark theme. Look at status labels on Tasks, Queue and Execution history | Theme changes at once and is kept after a restart; every coloured label also has text | Not run yet |
| MC-03 | Tasks > New task. Leave Name empty and move on; type `*/0 * * * *` in Cron; pick Once with a past time; fill in valid values and Save | A message appears under each wrong field as you go; the saved task appears in the list with its next run; a task whose module is missing shows "No handler" | Not run yet |
| MC-04 | Edit the task: change the schedule from cron to interval and save; Pause it; Resume it | The new schedule is used, earlier history stays; Pause and Resume change the status at once | Not run yet |
| MC-05 | Delete the task, confirm, then Restore it from the list. Try to delete a built-in task | Confirmation names the task; the task is hidden and comes back; built-in tasks have no Delete button | Not run yet |
| MC-06 | Create tasks A and B, make B depend on A, Run now A | B runs after A succeeds; making A depend on B is refused as a cycle | Not run yet |
| MC-07 | Pause the engine, Run now a task, open Queue, Cancel the waiting entry | Confirmation names the task; the entry disappears and is recorded as cancelled | Not run yet |
| MC-08 | Execution history: filter by task and status, open a failed run, press Print | The detail panel shows output, error, stack trace and log entries; the print preview shows the list | Not run yet |
| MC-09 | Error log: filter by text and date, open an entry | Stack trace shown | Not run yet |
| MC-10 | Configuration: set MaxRetryLimit to 11, then to 2; add a task override; switch the Console provider on at Warning; open This computer | 11 is refused with the allowed range; 2 is saved; the override is used by that task; provider change saved; folders shown | Not run yet |
| MC-11 | Open Modules and Help | Every module with its task types and pages; Help shows cron help, the state table and priority rules | Not run yet |
| MC-12 | Backup and restore: Back up now with all three databases; restore `inventory_demo` from it, typing RESTORE; close the app while a task runs | "Backup made and verified"; the restore needs the word; data returns to the backup; the app closes within ShutdownGraceSeconds + 15 s | Not run yet |
| MC-13 | Reports: show each of the five reports for the last 7 days, filter by task and status, Export CSV, Print | Results change with the filters; the status filter is not offered for Upcoming schedule; CSV opens in Excel with a header row | Not run yet |
| MC-14 | Inventory: add a product with a reorder level above its stock, open Low-stock list, Run check now; sign in as a Viewer and try to edit a product | The product is listed as low; the Viewer cannot change products | Not run yet |
| MC-15 | Managed apps > Maintenance Agent, Run health check now; stop the agent and run it again; start the agent | Status and the last check shown; with the agent stopped the run fails with "The Maintenance Agent is not running on port 5087." in the Error log | Not run yet |
| MC-16 | Sign out; open a page; sign in as a Viewer; wait for the session time-out (set SessionTimeoutMinutes to 1 first). Then select Register, create an account, activate it on the Users page and sign in with it; sign out, select Forgot password? and set a new password with its recovery code | The login page is shown; a Viewer sees no Configuration, Backup or Users; the session ends after the time-out; the registered account shows Waiting for approval and signs in only after activation; the recovery code sets the new password once and a new code is shown | Not run yet |
| MC-17 | With the Admin app running, start a second copy (for example a second build output folder). Queue 20 Run now runs of a short task in the first copy | The second copy shows "The scheduler engine is already running on …" and runs nothing; every run in the first copy has exactly one execution | Not run yet |
| MC-18 | Drop and recreate an empty `gmss` with `database\setup_windows.ps1`, start the Admin app, then restart it. In psql try `INSERT INTO scheduler.task_queue (...)` with status `Bogus`, a schedule without a task, and a second task named in other letter case | 13 tables with UUID keys and seeded settings created once; each invalid insert is refused by a CHECK, foreign-key or unique constraint | Not run yet |
| MC-19 | Backup and restore: back up two databases to a chosen folder with a copy folder set; damage one file of a set (change a byte) and try to restore it; set BackupRetentionCount to 2 and back up three times; restore the newest set on a second computer | Files verified and copied with matching checksums; the damaged set is refused; only two sets are kept; the second computer shows the same tasks and users | Not run yet |
| MC-20 | Set a processed queue entry, an execution and a login audit row older than the retention periods with psql `UPDATE`, then Run now the Log Clean-up task | The old queue entry, history and audit are removed; an execution whose queue entry was purged stays with an empty queue reference | Not run yet |
| MC-21 | Developer check: set the PostgreSQL tools folder to a wrong folder, add a new migration and start the Admin app | Start-up stops with the safety-backup message and the migration is not applied | Not run yet |
| MC-22 | Open a task in the editor, change the same task with psql `UPDATE scheduler.task SET description = 'x'`, then Save in the editor | The editor reports that someone else changed the task | Not run yet |
| MC-23 | Modules: Launch the Maintenance Agent, open Managed apps > Maintenance Agent, then Stop it there; Stop and Launch the Inventory Monitor; close the Admin app while the agent runs | The agent starts without a terminal and shows Running; Stop ends it; Inventory Stop pauses its three tasks and Launch resumes them and queues a stock report and a low-stock check; closing the Admin app ends the agent it launched | Not run yet |
| MC-24 | Modules: switch the Inventory Monitor off, open the menu and an old link to /inventory/products, Run now an Inventory task; switch it on again; restart the Admin app with it off; try to switch Login and Security off | Its menu entries disappear, the link shows "Module switched off", the run waits in the Queue; after switching on, the run happens and the menu is back; after a restart it is still off; Login and Security cannot be switched off | Not run yet |

## 4. Click-through checklist

Navigation tests check that every menu entry and link has a page. Buttons that change data are checked by hand.
Tick each one when it does what its label says.

| Page | Buttons and links | Checked |
|---|---|---|
| Header and menu | Every menu entry; Sign out; theme switch | ☐ |
| Dashboard | Pause engine, Resume engine, Refresh, a row in Recent executions | ☐ |
| Tasks | New task, Search, status filter, Pause, Resume or Enable, Disable, Delete (confirm and cancel), Restore, a task name | ☐ |
| Task editor | On a cron schedule, Every interval, Once, Use the example for this task type, dependency boxes, Save, Run now, Pause, Disable, Delete, Back to tasks | ☐ |
| Queue | Waiting, Claimed, Waiting and claimed, Cancel (confirm and cancel), Refresh | ☐ |
| Execution history | Apply, a row, Close, Print, Previous and Next page | ☐ |
| Error log | Search, Apply, a row | ☐ |
| Reports | Report list, From, To, Task, Status, Outcome, Export CSV, Print | ☐ |
| Backup and restore | Database boxes, Browse, Back up now, Restore (confirm with and without RESTORE), Refresh | ☐ |
| Configuration | Change, Save, Cancel, Add override, Use global value, provider switches and levels, This computer Save | ☐ |
| Modules, Help | Links on the page | ☐ |
| Users | Add user, Create user, Edit, Save (role and status changes, unlocking a locked account), Reset password, Deactivate (confirm and Keep active) | ☐ |
| Account | Change username with an incorrect password, an existing username and a valid new username; verify that the signed-in name updates and the new username can sign in. Change password with a wrong and a right current password. | ☐ |
| Inventory | Add product, Edit, Save, Delete (confirm and Keep), −1, +10, Refresh, Run check now | ☐ |
| Maintenance Agent | Refresh, Run health check now | ☐ |
