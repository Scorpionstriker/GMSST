# User manual

This manual explains how to use the Admin application of the Generic System Scheduler Template (GSST). Each section
covers one page of the menu. Screenshots are marked as placeholders and are added from the final build.

## 1. Signing in and roles

When the app starts for the first time it asks you to **create the first administrator** (username, password and
confirmation). After that, sign in with your username and password. After `MaxFailedLogins` wrong passwords in a row
(default 5) the account is locked for `LockoutMinutes` (default 15); the message is always "Invalid username or
password." so nobody can tell whether an account exists. You are signed out after `SessionTimeoutMinutes` without
activity (default 20).

**Recovery code.** Every account gets a recovery code such as `K7QM-2XNB-R9TD-HW4P` when it is created. It is shown
only once, so write it down. The scheduler runs locally without email, so this code is how you prove who you are when
you forget your password. It works once; each time you use it you get a new one. You can also create a new code on the
Change password page, which makes the old one stop working.

**Register.** Select **Register** on the sign-in page to ask for an account: choose a username and password and
note the recovery code. The account is a Viewer and cannot sign in until an administrator activates it on the Users
page.

**Forgot password.** Select **Forgot password?**, then enter your username, your recovery code and a new password
twice. Wrong codes count as failed sign-ins, so the same lockout applies, and the message never says which part was
wrong. If you have no recovery code, ask an administrator to reset your password.

| Role | Can do |
|---|---|
| Administrator | Everything: create, edit and delete tasks, run, pause and cancel, change configuration, back up and restore, manage users, change Inventory data |
| Viewer | Read every monitoring page, reports and Help; cannot change anything. Configuration, Backup and restore and Users are hidden |

> Screenshot placeholder: sign-in page.

If a red banner says "Login bypassed (developer mode)", the app was opened without sign-in for development. See
the installation guide, section 8.

## 2. Dashboard

The first page shows the engine status (running, paused or stopped, with the host name), counts of waiting and
busy workers, **Runs in the last 24 hours** by status, **Average duration per hour**, and **Recent executions**.
Administrators can **Pause engine** (no new work is claimed; running tasks finish) and **Resume engine**.
**Refresh** reloads the figures. If another copy of the scheduler already runs on the database, a banner names it.

> Screenshot placeholder: dashboard.

## 3. Tasks

The task list shows every task with its type, schedule, status and next run. Use **Search** and the status filter
to find tasks. Badges show **Built-in** tasks (created by the scheduler or a module; they can be edited, paused and
renamed but not deleted) and **No handler** (no installed module provides the task type, so it will not run).

Actions for administrators:

| Action | Effect |
|---|---|
| New task | Opens the task editor |
| Pause | Stops scheduled runs; Run now still works |
| Resume / Enable | Makes the task Active again |
| Disable | Switches the task off completely, including Run now and retries |
| Delete | Asks for confirmation; hidden for built-in tasks. Deleted tasks can be restored from the list |

> Screenshot placeholder: task list.

## 4. Task editor

| Field | Notes |
|---|---|
| Name | Required and unique |
| Task type | The installed task types; the description and an example payload are shown below it. **Use the example for this task type** copies the example |
| Description | Optional |
| Schedule | **On a cron schedule** (five fields: minute, hour, day of month, month, weekday; names such as MON and JAN are allowed), **Every interval** (seconds), or **Once** (a date and time that must be in the future) |
| Time zone | The schedule's time zone. Times that do not exist because of a daylight-saving change run at the first valid minute |
| Start and End | Optional; the task does not run outside them. Each has a date field (dd/mm/yy) and a time field (hh:mm, 24-hour); a date without a time means 00:00. A Once task needs both (Run at) |
| If runs were missed | Skip, Run once, or Run all (up to RunAllMisfireCap) for runs more than 60 seconds late |
| Priority | 1 = highest, 10 = lowest. Higher priority runs first when several tasks are due |
| Retries after a failure, Time-out (seconds) | Empty uses the global settings |
| Depends on | Tasks that must succeed first; cycles are refused |
| Payload (JSON) | Settings for the task type, as a JSON object |

Each field is checked as you leave it, and the cron field is checked while you type, with the error shown under
the field. **Save** checks everything again. **Run now** queues one run immediately, even for a paused task.

> Screenshot placeholder: task editor with a cron schedule.

## 5. Queue

Shows work that is **Waiting** or **Claimed** (being run), with priority, due time, attempt, trigger (Scheduled,
Manual, Retry, Dependency or CatchUp) and the worker. The filter buttons choose Waiting, Claimed or both.
Administrators can **Cancel** a waiting entry. **Refresh** reloads the list.

## 6. Execution history

Every run with its status (Running, Success, Failed, Retrying, TimedOut or Cancelled), trigger, start time,
duration, retry number and host. Filter by task, status and date range, then **Apply**. Dates are typed as
dd/mm/yy (for example 30/10/26) or chosen with the calendar icon; From starts at the beginning of that day and To
includes the whole day. Leave a date empty for no limit. The Error log and Reports date fields work the same way. Click a row to see its
output, error and log entries. **Print** prints the current list.

> Screenshot placeholder: execution history with the detail panel open.

## 7. Error log

Errors from failed runs, newest first, with time, task, error type, message, the run's status and the retry number.
Filter by task, text and date range. Select a row for the technical details, including the stack trace.

## 8. Reports

Choose a report, a date range (up to 366 days) and optional filters, then view, **Print** or **Export CSV**:

| Report | Shows | Filters |
|---|---|---|
| Execution summary | Runs per task and status, success rate | Task, status |
| Failures and errors | Failed and timed-out runs with their errors | Task, status |
| Performance | Average, shortest and longest duration per task | Task, status |
| Upcoming schedule | The next runs of active tasks | Task |
| Security and audit | Sign-ins, lockouts, user changes and login bypass changes | Outcome (succeeded or failed) |

## 9. Backup and restore (administrators)

The page shows where the PostgreSQL tools were found and lists the backup sets in the chosen folder.

- **Back up now**: tick the databases (`gmss`, `inventory_demo`, `maintenance_demo`; all by default), optionally
  choose another folder, and start. The message says "Backup made and verified" only when every file passed the
  check; otherwise a warning names the file.
- **Restore**: choose a set, tick the databases to restore and type `RESTORE` to confirm. A safety backup is taken
  first, the engine is paused during the restore, and each database is restored completely or not at all.
  Restoring `gmss` signs you out. Stop the Maintenance Agent before restoring `maintenance_demo`.

The built-in **Database Backup** task backs up all three databases every night at 01:00 local time.

> Screenshot placeholder: backup page.

## 10. Configuration (administrators)

- **Scheduler settings**: each setting with its value, default and allowed range. **Change**, enter a value,
  **Save**. Values outside the range are refused with a message.
- **Task overrides**: give one task its own retry limit or retry delays (**Add override**, **Use global value**).
- **Logging providers**: switch Database, File, Console and module providers on or off and set the minimum level.
- **This computer**: folders for backups, the backup copy and logs, the PostgreSQL tools folder and the Maintenance
  Agent address. Only a Developer super-admin can see and change the developer login bypass. These are saved in the
  user settings file on this computer.

## 11. Modules

Lists the loaded modules with their description and the pages they add, and every task type with the module that
provides it. The first module is the Scheduler Admin GUI, the administration application itself, which provides the
core pages (Dashboard to Help); it is always on and shows as Running. Administrators also control the modules here:

| Control | What it does |
|---|---|
| **On** slider | Switches a module off or on. A switched-off module stays installed but is disconnected: its menu entries and pages are hidden, tasks of its types wait instead of running, and its application is stopped. Switching it on reconnects it and launches its application again if it was running. Every module is on by default, and the choice is remembered on this computer. The Scheduler Admin GUI and Login and Security are always on. |
| **Launch** / **Stop** | Starts or stops the module's application. For the Maintenance Agent connector this starts the separate Maintenance Agent program in the background (no terminal needed) and stops it again; for the Inventory Monitor it resumes or pauses its three scheduled tasks, and Launch also runs a stock report and a low-stock check straight away. |

The first launch of the Maintenance Agent builds it, which can take up to a minute. An agent launched from the
Admin application is stopped when the Admin application closes.

If the Maintenance Agent is running when the Scheduler closes, the Scheduler asks before stopping it. Cancel leaves
both applications running; **Stop Agent and Close** shuts down the agent before closing the Scheduler.

## 12. Help

Getting started, cron syntax with examples, the task state table, priority and dependency rules, and what each
status means.

## 13. Inventory pages (demo app 1)

| Page | Use |
|---|---|
| Products | List, **Add product**, **Edit** and **Delete** products (SKU, name, quantity on hand, reorder level, unit price) |
| Stock levels | Current stock with **Adjust** buttons (−1, +10) and the recent hourly stock reports |
| Low-stock list | Products below their reorder level and the shortfall; **Run check now** runs the low-stock task |

Changes need the Administrator role.

## 14. Maintenance Agent page (demo app 2)

**Managed apps > Maintenance Agent** shows whether the separate Maintenance Agent application is running, since
when, its database, the last health check and the last clean-up. The health check tests the Scheduler and Inventory
databases as well as its own database, disk and memory; its summary and failure details are recorded in the Scheduler's
execution log. **Run health check now** runs the health-check
task. If the agent is not running, the page says so and offers **Launch Maintenance Agent**; while it runs,
**Stop Maintenance Agent** stops it. If the agent is not running, the health-check task fails with a clear message in
the Error log.

## 15. Users (administrators)

Add users (**Add user**: username, role, password and confirmation), **Edit** a user's role, **Reset password**,
**Deactivate** or reactivate an account, and see locked accounts with "Locked until …". Every change is recorded in
the Security and audit report. You cannot deactivate or demote the last active administrator.

Accounts registered on the sign-in page show **Waiting for approval**. Select **Edit**, tick **Active** and **Save**
to let the person sign in; change the role to Administrator first if they need it. When you add a user, the page
shows their recovery code once: give it to them with their password.

## 16. Change password

Enter the current password and the new one twice. Passwords need at least 10 characters.

Under **Recovery code**, enter your current password and select **Create a new recovery code** to get a new code for
"Forgot password?". Accounts that existed before recovery codes were added have none until you create one here.

## 17. Common tasks

| I want to … | Do this |
|---|---|
| Run a task once now | Tasks > open the task > **Run now** |
| Stop a task for a while | Tasks > **Pause**; **Resume** later |
| See why a run failed | Error log, or Execution history > open the run |
| Run task B after task A | Edit B > Depends on > choose A |
| Keep a copy of backups on a USB drive | Configuration > This computer > Backup copy folder |
| Give a colleague read-only access | Users > Add user > role Viewer |
