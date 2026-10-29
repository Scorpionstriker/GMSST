# How the analysed defects are handled

The code analysis listed 28 defects (D1 to D28) in the scheduler used as a design guide. This table shows how
each one is fixed or avoided in GSST, and where it is checked. "Not applicable" means the feature that had the
defect was not carried over, usually because GSST is fully local and has no network API.

| # | Defect | How GSST handles it | Checked by |
|---|---|---|---|
| D1 | Non-UTC time zones made next-run calculation fail | Cron times are built as wall-clock times in the schedule's zone and converted to UTC with `TimeZones`; gaps and repeated hours at clock changes are handled | `CronExpressionTests` (Johannesburg, London summer time, clock changes) |
| D2 | `*/0` looped forever | A step must be at least 1; the parser rejects it with a message | `CronExpressionTests.ZeroStep_DoesNotHang` |
| D3 | Engine busy-looped while a job was running | The adaptive delay only counts waiting entries a free worker could claim; it sleeps between 0.5 s and the poll limit and wakes early on a signal | `ConcurrencyTests.Engine_DoesNotSpinWhileTheOnlyWorkerIsBusy` |
| D4 | Catch-up rule inverted | `ScheduleCalculator.PlanDueRuns` applies Skip, Run once and Run all (capped by `RunAllMisfireCap`) to runs more than 60 s late | `ScheduleCalculatorTests`, `EngineTests.MissedRuns_FollowTheMisfirePolicy` |
| D5 | Shell task ran any command, HTTP task could reach any host, API had no sign-in | No shell task type; the HTTP task type calls only localhost and does not follow redirects; no network API; sign-in through the Login and Security module | `HandlerTests`, `SecurityTests` |
| D6 | Validators never registered | Validators and use cases are found by scanning the Application assembly and wrapped in validation, authorisation and logging decorators | `ApplicationWiringTests` |
| D7 | Time zone ignored when rescheduling and in recovery | Every calculation goes through `ScheduleSpec`, which always carries the schedule's zone | `CronExpressionTests`, `UseCaseTests.CreateTask_InALocalTimeZone_StoresUtcAndCalculatesTheNextRun` |
| D8 | Time zone not returned to the edit form | `GetTaskQuery` returns the full schedule; the editor shows and keeps the zone, and an unchanged schedule is saved exactly as loaded | `UseCaseTests.CreateTask_InALocalTimeZone_StoresUtcAndCalculatesTheNextRun`, task editor checked in a browser |
| D9 | Lease could expire during a long job | The claim lasts the task's time-out plus `LeaseGraceSeconds`; the run is cancelled at its time-out, so a lease only expires if the host died, and recovery then re-queues or records the run | `EngineTests.Recovery_RetriesRunsInterruptedByACrash_AndReleasesUnstartedClaims` |
| D10 | Unbounded parallel runs | A fixed worker pool of `MaxConcurrency` workers per host | `ConcurrencyTests` |
| D11 | No cancellation, time-outs or graceful shutdown | Each run has its own cancellation with the task's time-out; stopping or pausing gives running tasks `ShutdownGraceSeconds`, then cancels and re-queues them; a handler that ignores cancellation is abandoned | `EngineTests.Stop_CancelsRunningTasksAndQueuesThemAgain`, `EngineTests.HandlerThatIgnoresItsTimeOut_IsAbandonedAndRecordedAsTimedOut` |
| D12 | No retries | `RetryPolicy`: exponential back-off from `RetryBaseDelaySeconds` up to `RetryMaxDelaySeconds`, up to the task's or the global retry limit | `RetryPolicyTests`, `EngineTests.FailedRun_IsRetriedWithBackOffAndFinallyMarkedFailed` |
| D13 | Misconfigured tasks reported success | Built-in and demo handlers check their payload and return a failure with a clear message | `HandlerTests` |
| D14 | Event publishing task without a processor; unused message bus | Not applicable: events are published in-process through `ISchedulerEvents` | |
| D15 | Architecture tests could never fail | Rules use the real namespaces, and positive-control tests prove that an existing dependency is detected | `LayerDependencyTests` |
| D16 | Reports silently limited and filtered in the browser | Reports are aggregated in PostgreSQL for a range of up to 366 days; a banner says when the detail table is cut | Reports page checked in a browser |
| D17 | End time never enforced | Start and end times apply to every schedule type | `ScheduleCalculatorTests.EndTime_StopsTheSchedule` |
| D18 | Manual run un-paused a job; trigger on a running job lost | Run now queues a separate Manual entry and leaves the task's status unchanged; it is published as an event | `EngineTests.RunNow_ExecutesTheTaskOnceAndRecordsSuccess` |
| D19 | Form fields dropped and server errors hidden | Every editor field maps to `TaskInput`; each validation message appears beneath its field and other errors in a banner | `UseCaseTests.CreateTask_ReportsEveryInvalidFieldThroughTheRegisteredValidator`, task editor checked in a browser |
| D20 | Cron builder offered unsupported syntax; vague errors | Names (JAN, MON), 7 for Sunday and standard day matching are supported; each error names the field and the allowed range; Help lists only supported syntax | `CronExpressionTests` |
| D21 | Event subscriptions never disposed | One in-process event hub with a bounded queue; every page unsubscribes when it closes | Admin pages (code review) |
| D22 | `EnsureCreated` blocked migrations | Only migrations are used, under a PostgreSQL advisory lock, with a backup first when upgrading | `DatabaseTests.Initializer_CanRunAgainWithoutDuplicatingSeedData` |
| D23 | Dead or unwired code | Not carried over | |
| D24 | Health check always healthy | Not applicable (no web endpoint); the Maintenance Agent's health check tests the database, disk and memory, and the Admin app shows a clear message when PostgreSQL cannot be reached | Maintenance Agent |
| D25 | Dashboard and statistics computed in memory; export unsorted and uncapped | Aggregates run in PostgreSQL; CSV export is newest first and capped at 100,000 rows | `DatabaseTests.ReadQueries_TranslateToSql` |
| D26 | Leftover settings and mismatched names | Not carried over | |
| D27 | Every failure returned the same status | Not applicable (no web API); every use case returns a `Result` with an error kind (Validation, NotFound, Forbidden, Conflict, Failure) and per-field messages | `ApplicationWiringTests`, `UseCaseTests` |
| D28 | Documentation promised providers that do not exist | The READMEs describe only what is built | |
