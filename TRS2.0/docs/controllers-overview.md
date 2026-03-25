# Controllers overview

## Objective

This document describes the responsibility of every controller under `TRS2.0/Controllers`, the business processes each one orchestrates, and the type of interaction they expose to the MVC application or to the frontend.

## General conventions

* Controllers in this layer combine three responsibilities:
  * build MVC views and view models;
  * expose JSON endpoints consumed by interactive screens;
  * trigger operational services such as imports, reminders, PM calculations or alarm evaluation.
* Most business rules live in services such as `WorkCalendarService`, `LoadDataService`, `ReminderService` and the alarm services. Controllers are mainly responsible for:
  * validating the request context;
  * scoping the data to the current user role;
  * assembling the response model;
  * persisting simple updates requested by the UI.
* The largest controllers are `ProjectsController`, `TimesheetController`, `PersonnelsController` and `AdminController`. They act as orchestration controllers for the main operational flows of TRS.

## Authentication and entry points

### `AccountController`

**Responsibility**

* Manages the identity lifecycle of TRS users.
* Bridges ASP.NET Identity with the `Personnel` table.
* Sends registration, recovery and admin-reset emails.

**Main processes**

* **Login flow**: validates the `ApplicationUser`, checks whether the user is an admin or a personnel-linked account, signs in through Identity, and records the first login of the day in `UserLoginHistories` for personnel users.
* **Self-registration flow**: validates that the user already exists in `Personnel`, generates a valid password, stores it in `Personnel`, creates the Identity account, sends the onboarding email and assigns the default `Researcher` role.
* **Recovery flow**: generates password reset tokens, sends reset links, and applies the password reset submitted by the user.
* **Password maintenance**: supports normal password changes for authenticated users and administrative resets for existing accounts.

### `HomeController`

**Responsibility**

* Serves the generic entry pages of the application.

**Main processes**

* Returns the `Index`, `Privacy`, `Welcome` and generic `Error` views.
* Builds the `ErrorViewModel` with the current request identifier.

### `DiagnosticController`

**Responsibility**

* Exposes a lightweight infrastructure diagnostic endpoint.

**Main processes**

* Returns the configured `DefaultConnection` connection string so support teams can verify which database is currently targeted by the runtime configuration.

### `ErrorController`

**Responsibility**

* Serves error views that are reached through explicit routing rather than through a business controller.

**Main processes**

* Returns the `AccessDenied` view used by authorization failures.

## Alarm and notification controllers

### `AlarmsController`

**Responsibility**

* Provides the API consumed by the UI to retrieve the alarms of the authenticated user.

**Main processes**

* Resolves the current `ApplicationUser` through `UserManager`.
* Delegates alarm evaluation to `UserAlarmService`.
* Returns the active alarms and their total count as JSON.

### `AlarmCenterController`

**Responsibility**

* Renders operational alarm screens for roles that must act on the result.

**Main processes**

* Loads the current user and role set.
* Uses `OutOfContractAssignedEffortService` to obtain effort assignments that fall outside a valid contract window.
* Builds `OutOfContractAssignedEffortPageViewModel`, including navigation links to the detailed project-person effort view.

## Project management controllers

### `ProjectsController`

**Responsibility**

* Central controller for project administration.
* Manages project catalog data, personnel assignment, effort planning, report periods, monthly locks and project exports.

**Main process groups**

* **Project catalog and detail**: lists active projects, shows management roles (`PM`, `PI`, `FM`) and calculates work-package totals such as distributed effort and covered effort.
* **Project personnel maintenance**: manages `Projectxpeople` links, toggles `Wpxperson` assignments and scopes the selection view used to relate personnel with project work packages.
* **Project effort planning**: builds the project effort plan by work package, and the personnel effort grid by person/month/WP, including PM limits, total effort per month, lock states and partial-dedication warnings.
* **Effort persistence**: saves monthly personnel effort updates and enforces capacity rules, PM limits and project-boundary adjustments before writing `Persefforts`.
* **Cross-project person view**: shows all effort assignments of a single person across overlapping projects during the selected root project period.
* **Report period administration**: creates report periods by exact dates or by month labels, deletes periods and renders the detail panel for a period.
* **Period compliance detail**: for each person in a report period, calculates declared hours, total available hours, hours inside the project, completion percentage, overload/out-of-contract flags, locks and signature login dates.
* **Project locks**: creates or updates `ProjectMonthLock` records to lock or unlock one person, one project and one month.
* **Project exports**: generates CSV files for PM by WP, PM audit, worked days, estimated worked days, declared hours per WP, estimated hours per WP and worked days per WP.
* **Period rates**: builds the report-period rate grid in estimated mode and in timesheet mode using `PersonRates`, `PersonManualRates`, project effort and declared hours.

### `WpsController`

**Responsibility**

* Maintains work-package master data and the project-level planned effort distribution stored in `Projefforts`.

**Main processes**

* Supports standard MVC CRUD for work packages.
* Updates WPs from the interactive project maintenance screen.
* Prevents date-range reductions that would leave positive personnel effort outside the new WP range.
* Saves monthly planned effort either for one WP or for a bulk set of WPs.

### `ProjectManagerController`

**Responsibility**

* Concentrates the operational tools used by project managers to import timesheets and report incidents.

**Main processes**

* **File upload session**: stores uploaded Excel files in a temporary per-user folder and removes the folder after processing.
* **Excel processing**: parses the uploaded timesheet layout, resolves the person, project and WP represented in each row, writes `Timesheets`, and calls `AdjustEffortAsync` to keep planned effort consistent with declared hours.
* **Issue tracking**: writes import problems to `TimesheetErrorLogs` without duplicating equivalent records.
* **Error reporting**: prefills the error-report form from the authenticated user and sends an operational incident email with optional attachment.

## Timesheet and personnel controllers

### `TimesheetController`

**Responsibility**

* Manages monthly timesheet capture, PDF generation, signature dates and automatic completion.

**Main process groups**

* **Role-scoped timesheet access**: allows admins and project managers to open any person, researchers to open their own timesheets and leaders to open scoped personnel records.
* **Monthly timesheet assembly**: loads leaves, partial leave reductions, travels, holidays, assigned effort, timesheet entries and project locks to build the `TimesheetViewModel`.
* **Hour persistence**: saves daily hours for each person/WP/day combination in bulk.
* **PDF generation**: builds the standard PDF and the signed PDF variant, including travel tables, work-package summaries and signature sections.
* **Signature date lifecycle**: validates manual signature dates, writes `ManualLoginDate` when needed and resolves the effective login date for investigator and responsible manager signatures.
* **Automatic completion**: checks whether the month contains effort in a single WP and, if so, delegates timesheet autofill to `WorkCalendarService`.

### `PersonnelsController`

**Responsibility**

* Maintains personnel master data and exposes the operational views around calendars, dedications, external rates, travels and login history.

**Main process groups**

* **Personnel catalog and detail**: standard MVC CRUD plus search endpoints used by the operational UI.
* **Calendar view**: returns national holidays, travels and leaves in the event format expected by the calendar frontend.
* **Dedication maintenance**: exposes dedication history, updates dedication intervals, removes intervals and appends new historical dedication records.
* **Manual external rates**: builds rate segments that align with affiliation and dedication changes, validates the required affiliation/hour setup and stores the resulting `PersonManualRates`.
* **Travel follow-up**: shows all travels of a person, pending travels, and pending travels scoped to the projects managed by the current admin or project manager.
* **Travel approval and cancellation**: updates travel status and delegates liquidation reprocessing to `LoadDataService`.
* **Login log**: builds the yearly first-login report for personnel that have both active contracts and effort in open projects.

## Administration and operational tooling

### `AdminController`

**Responsibility**

* Concentrates administrative maintenance actions and operational recovery tools.

**Main process groups**

* **Dashboard and role management**: loads PM values, recent process logs, users and roles; reassigns roles to users.
* **PM calibration**: creates daily PM values for one month or for a full year and exposes PM calculations for one person/date.
* **Batch Excel import**: processes folders of Excel timesheets, logs errors to files and moves problematic files to a review folder.
* **Operational logs**: returns available log files and their content through controlled endpoints.
* **Effort and timesheet correction**: adjusts effort for one person/WP/month, launches bulk timesheet autofill and generates the CSV review report for automatic adjustments.
* **Operational job launchers**: executes scheduled data-load jobs on demand.
* **Overload correction**: checks whether one month is overloaded for a person, adjusts the overload manually, or launches the bulk overload process from a given start date.
* **Role repair**: assigns the default `Researcher` role to users that have no role at all.
* **Reminder tooling**: sends single-user reminder tests, runs weekly reminder dry-runs and exposes the reply-to smoke test.
* **Rates generation**: launches the manual regeneration of `PersonRates` and records the result in `ProcessExecutionLogs`.

### `LoadDataController`

**Responsibility**

* Exposes manual endpoints that trigger Quartz jobs for data loading and maintenance.

**Main processes**

* Triggers PM recalculation, liquidation import, liquidation processing, personnel import, affiliation/dedication import, group import, leader import and project import.
* Launches agreement-event synchronization, user/personnel reconciliation, leave refresh, investigator timesheet autofill, out-of-contract detection, global effort adjustment and person-rate generation.
* Builds the `JobDataMap` required by `LoadDataServiceJob`, including source file paths when the job consumes a text file.

### `ToolsController`

**Responsibility**

* Builds global operational dashboards that compare capacity, hours and effort.

**Main processes**

* **Global hours**: for each person with active contract in the selected year, calculates the monthly theoretical working hours using cached calendar data.
* **Global effort**: compares assigned effort versus the monthly PM cap for each person in the selected year.
* **Breakdown APIs**: returns the per-project and per-WP effort breakdown used by client-side drill-down views.
