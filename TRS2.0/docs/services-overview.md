# Services overview

## Objective

This document describes the responsibility of every class under `TRS2.0/Services` and `TRS2.0/Services/Alarms`, with emphasis on the business processes they orchestrate and the boundaries they enforce.

## Core services

### `EmailSender`

**Responsibility**

* Sends HTML emails through the configured SMTP server.
* Supports reply-to, sender display name, BCC copies and file attachments.
* Adds SMTP headers that mark the message as automatically generated.

**Main process**

1. Build the SMTP client from `SmtpSettings`.
2. Build the `MailMessage` with sender, recipients and headers.
3. Add attachments when provided.
4. Send the message asynchronously.

### `LoadDataService`

**Responsibility**

* Acts as the operational ingestion and synchronization service for TRS.
* Loads source data from files and external endpoints into TRS domain tables.
* Executes maintenance jobs such as PM recalculation, leave synchronization, timesheet autofill and overload adjustments.
* Implements `Quartz.IJob`, so it can be scheduled as an automated background process.

**Relevant process groups**

* **PM maintenance**: recalculates `PersMonthEffort` values by scanning each person and every relevant month derived from dedication history.
* **Liquidation ingestion**: imports travel/liquidation data from text files, detects existing records and updates status transitions.
* **Personnel and contract ingestion**: loads personnel, affiliations, dedications, personnel groups, leaders and projects from source files.
* **Agreement and leave synchronization**: downloads agreement/leave information from external systems and persists normalized records.
* **Token lifecycle management**: stores, reloads and renews the external bearer token required by the integration endpoints.
* **Timesheet automation**: autofills monthly sheets for eligible people and adjusts overload situations in bulk.
* **Scheduled execution**: orchestrates the recurring batch operations executed by Quartz.

### `ReminderEmailOptions`

**Responsibility**

* Centralizes reminder email configuration.
* Controls the reply-to address, visible sender name and whether the assignment cap is applied to the required-hours threshold.

### `ReminderService`

**Responsibility**

* Decides who must receive initial or weekly reminder emails.
* Computes pending months, required thresholds and declared hours.
* Supports dry-run evaluation for admin screens and alarm rules.

**Main processes**

* **Monthly initial communication**: on the first run of the month, the service scans active personnel with email, computes every pending historical month and sends the initial message plus the user guide.
* **Weekly follow-up**: on subsequent runs, the service evaluates the previous month only and sends reminders to users who remain below the required threshold.
* **Single-user validation flow**: allows sending the same logic to a single person for validation or controlled testing.
* **Dry-run analysis**: exposes `ReminderCandidate` projections so alarms and admin tools can inspect who would receive a reminder without sending it.
* **Threshold calculation**: combines declared hours, work-calendar capacity and optional assignment-cap logic.

### `RoleService`

**Responsibility**

* Encapsulates role assignment through ASP.NET Identity.
* Verifies user existence and role existence before performing the operation.

### `UserAlarmService`

**Responsibility**

* Aggregates all alarm rules registered in dependency injection.
* Builds the evaluation context from the authenticated user and their roles.
* Returns the active alarms ordered by severity.

## Alarm subsystem

The alarm subsystem is intentionally split into:

* **rule classes**, which decide whether an alarm must be shown to the current user;
* **query services**, which gather the data required by the rules;
* **context contracts**, which provide a common evaluation model.

### `IUserAlarmRule`

**Responsibility**

* Defines the contract implemented by every alarm rule.

### `UserAlarmContext`

**Responsibility**

* Provides the current `ApplicationUser`, the resolved role set and helper predicates such as `IsInAnyRole`.

### `CurrentMonthNoHoursAlarmRule`

**Responsibility**

* Detects researchers with current-month effort assigned but no declared hours after the early part of the month.

**Main process**

1. Validate that the user is linked to personnel data and has the `Researcher` role.
2. Calculate current-month assigned effort.
3. Ignore users with no effort or before the configured day threshold.
4. Read declared hours for the current month.
5. Raise an informational alarm when declared hours are still zero.

### `InactiveContractAlarmRule`

**Responsibility**

* Warns admins and project managers when their personnel record has no active dedication on the current date.

### `OutOfContractAssignedEffortService`

**Responsibility**

* Builds the list of effort assignments that fall outside an active contract window for the previous and current month.

**Main process**

1. Scope the visible projects according to the viewer role.
2. Aggregate positive effort by month, project and person.
3. Load the dedication intervals for all affected people.
4. Keep only assignments with no overlapping contract in the corresponding month.

### `OutOfContractAssignedEffortAlarmRule`

**Responsibility**

* Converts the result of `OutOfContractAssignedEffortService` into a user-facing danger alarm with totals per month, project and person.

### `PendingPreviousMonthTimesheetAlarmRule`

**Responsibility**

* Reuses `ReminderService` logic to expose a danger alarm for an incomplete previous-month timesheet.

### `PendingTravelApprovalService`

**Responsibility**

* Returns travel liquidations in pending approval state.
* Applies role-based scoping:
  * admins can see all pending items;
  * project managers see only items linked to their projects.

### `PendingTravelApprovalAlarmRule`

**Responsibility**

* Builds the warning alarm shown to admins and project managers when pending travel approvals exist.

## Refactoring principles applied in this pass

* Removed informal comments and markers that did not belong to a production codebase.
* Replaced them with focused XML summaries and business-oriented explanations.
* Normalized several service contracts and helper classes to improve readability.
* Fixed namespace imports and minor implementation details in the alarm and role services to make the code safer and more maintainable.
