# User acceptance test plan

This plan describes how the Generic System Scheduler Template (GSST) is evaluated by its intended users (DR-06).
The results are recorded in [UAT_Report.md](UAT_Report.md).

## 1. Objectives

| Quality | Question | Measure |
|---|---|---|
| Usability | Can a new user complete common scheduling work without help? | Each scenario completed without help in under 5 minutes; System Usability Scale (SUS) score of at least 68 (NFR-05) |
| Reliability | Does the system keep working during normal use? | No crash or lost work during any session (NFR-01, NFR-02) |
| Effectiveness | Does the system do what the user needed? | Every scenario reaches its expected result |

## 2. Participants

3 to 5 people who are not part of the development, for example classmates studying IT and the project supervisor.
At least one should not have used a task scheduler before.

| # | Name or code | Background | Role used | Date |
|---|---|---|---|---|
| P1 | *(to be filled in)* | | Administrator | |
| P2 | *(to be filled in)* | | Administrator | |
| P3 | *(to be filled in)* | | Administrator | |
| P4 | *(optional)* | | Viewer for scenario 1, then Administrator | |
| P5 | *(optional)* | | Administrator | |

Participants are told that the system is being tested, not them, that they may stop at any time, and that only
their results and comments are recorded, under a code.

## 3. Environment

- The developer's Windows laptop, with GSST installed by following the [Installation guide](Installation_Guide.md).
- PostgreSQL 18 running locally; the Admin app and the Maintenance Agent started before the session.
- Sample data: the Inventory products, the built-in tasks and the demo tasks created at first start, plus one user
  account per participant.
- A stopwatch, this plan printed, and the questionnaire in section 6.

## 4. Procedure

1. Explain the purpose of the system in two minutes and show the menu. Give no further training.
2. Hand over the scenarios one at a time. The participant thinks aloud; the observer notes time, help needed,
   errors and comments, and does not help unless the participant is stuck for two minutes.
3. After the last scenario the participant fills in the SUS questionnaire and the open questions.
4. Each session takes about 45 minutes.

## 5. Scenarios

| # | Scenario | Expected result | Requirements |
|---|---|---|---|
| S1 | Sign in with the account you were given | The Dashboard opens and shows your name | FR-45, FR-57 |
| S2 | Create a task "Daily note" of type Log Entry that runs every weekday at 07:30 | The task is saved and its next run is shown; the preview lists five weekday times | FR-01, FR-09, FR-16 |
| S3 | Run "Daily note" now and find the result | A successful run appears in Execution history with its output | FR-02, FR-58 |
| S4 | A run failed: find out why | The participant opens the failed "Maintenance: health check" run (agent stopped by the observer) in the Error log and reads the message | FR-59, FR-30 |
| S5 | Pause "Daily note" so it does not run tomorrow, then resume it | Status changes to Paused, then Active | FR-03 |
| S6 | Make "Inventory: weekly summary" run only after "Inventory: hourly stock report" has succeeded | The dependency is saved; a run of the stock report is followed by the summary | FR-26 |
| S7 | Produce a failures report for the last 7 days and save it as CSV | The report shows the failed runs; the CSV opens in Excel | FR-64, FR-66 |
| S8 | Make a backup of all databases, then restore the Inventory database from it | "Backup made and verified"; the restore asks for RESTORE and completes | FR-67, FR-72 |
| S9 | A product is running low: find it and run the low-stock check | The product appears in the Low-stock list after the check | FR-78 |
| S10 | Check that the Maintenance Agent is healthy | The Maintenance Agent page shows the agent running and a recent healthy check | FR-79 |

## 6. Questionnaire

### 6.1 System Usability Scale

Each statement is answered from 1 (strongly disagree) to 5 (strongly agree).

1. I think that I would like to use this system frequently.
2. I found the system unnecessarily complex.
3. I thought the system was easy to use.
4. I think that I would need the support of a technical person to be able to use this system.
5. I found the various functions in this system were well integrated.
6. I thought there was too much inconsistency in this system.
7. I would imagine that most people would learn to use this system very quickly.
8. I found the system very cumbersome to use.
9. I felt very confident using the system.
10. I needed to learn a lot of things before I could get going with this system.

Score: for odd items subtract 1 from the answer; for even items subtract the answer from 5. Add the ten results
and multiply by 2.5, giving 0 to 100. A score of 68 or more is above average.

### 6.2 Open questions

1. What was the easiest part of the system to use?
2. What was the most difficult or confusing part?
3. Was any message or label unclear? Which one?
4. Would you trust this system to run your own scheduled jobs? Why or why not?
5. What one change would make it better?

## 7. Success criteria

The system is accepted when:

- every scenario reaches its expected result for every participant;
- at least 80 per cent of scenario attempts are completed without help, each in under 5 minutes;
- the average SUS score is at least 68;
- no crash, lost task or data loss occurs during the sessions.

Issues found are recorded in the report with their severity, and fixes are tested again before submission.
