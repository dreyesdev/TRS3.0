# Reminders de Timesheet y sistema de alarmas de usuario

## 1) Cálculo exacto de horas para emails de reminder

El reminder semanal compara **horas declaradas** vs **umbral requerido** en el **mes anterior**.

### Paso a paso (lógica real implementada)

1. Se calcula el mes objetivo:
   - Si hoy es enero, se usa diciembre del año anterior.
   - Si no, se usa el mes anterior al actual.
2. Se filtran personas con:
   - email informado,
   - contrato activo en la fecha de envío (`Dedication.Start <= hoy <= Dedication.End`).
3. Para cada persona se valida que tenga asignación útil ese mes:
   - al menos un `Perseffort.Value > 0`,
   - y que el WP de esa asignación esté activo en ese mes.
4. Se calculan horas declaradas:
   - suma de `Timesheet.Hours` del mes para esa persona.
5. Se calculan horas requeridas base:
   - `WorkCalendarService.CalculateDailyWorkHoursWithDedicationAndLeaves(...)`
   - suma de horas diarias (ya contempla dedicación, festivos y bajas).
6. Se calcula umbral final requerido:
   - si `ReminderEmailOptions.UseAssignmentCap == false` → umbral = horas requeridas base.
   - si `true`:
     - `assignedFraction = SUM(Perseffort.Value del mes)` limitado a `[0,1]`.
     - umbral = `horasBase * assignedFraction`, redondeado a 1 decimal.
     - si `assignedFraction <= 0`, el umbral es `0` (no se envía reminder).
7. Regla final de envío:
   - se envía si `umbral > 0` **y** `declaradas < umbral`.

### Fórmula resumida

- `DeclaredHours = SUM(Timesheet.Hours en el mes)`
- `RequiredBaseHours = SUM(HorasDiariasCalculadasConDedicaciónYBajas)`
- `AssignedFraction = clamp(SUM(Perseffort.Value), 0, 1)`
- `RequiredThreshold = UseAssignmentCap ? round(RequiredBaseHours * AssignedFraction, 1) : RequiredBaseHours`
- **Pendiente** si `DeclaredHours < RequiredThreshold` y `RequiredThreshold > 0`

---

## 2) Sistema de alarmas junto al perfil

Se ha implementado un sistema inicial de alarmas con icono de campana en la barra superior.

### Comportamiento UI

- Se muestra un icono `bell` al lado del nombre de usuario.
- Si hay alarmas activas, aparece badge con contador.
- Al abrir el desplegable, cada alarma muestra:
  - título,
  - severidad,
  - descripción,
  - enlace directo para resolver.

### Arquitectura

- `UserAlarmService` ahora solo orquesta: obtiene roles del usuario y ejecuta reglas externas (`IUserAlarmRule`).
- Cada alarma vive en una clase independiente dentro de `Services/Alarms` y decide si aplica según roles.
- Endpoint API disponible: `GET /api/alarms/me`.

### Alarmas incluidas en esta primera versión

1. `timesheet.pending.previous_month` (danger)
   - Regla: `PendingPreviousMonthTimesheetAlarmRule`.
   - Roles: `Researcher`, `ProjectManager`, `Leader`, `User`.
2. `dedication.contract.inactive` (warning)
   - Regla: `InactiveContractAlarmRule`.
   - Roles: `Admin`, `ProjectManager`.
3. `timesheet.current_month.no_hours` (info)
   - Regla: `CurrentMonthNoHoursAlarmRule`.
   - **Escenario de prueba actual**: solo para `Researcher`.

### Extensión recomendada

Para añadir nuevas alarmas:
1. Crear una clase que implemente `IUserAlarmRule`.
2. Implementar la lógica de rol + consulta y devolver `UserAlarmViewModel` cuando aplique.
3. Registrar la regla en `Program.cs` con `AddScoped<IUserAlarmRule, ...>()`.
4. (Opcional) Exponer configuración por `appsettings` para activar/desactivar reglas por entorno.
