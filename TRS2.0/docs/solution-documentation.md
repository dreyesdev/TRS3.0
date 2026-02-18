# TRS 3.0 – Documentación Integral de la Solución

> Documento corporativo de referencia funcional y técnica del sistema TRS.
>
> **Ámbito**: arquitectura, módulos, modelo de datos, flujos críticos, seguridad, operación y gobernanza del sistema de recordatorios/alertas.

---

## 1. Resumen ejecutivo

TRS 3.0 es una aplicación web ASP.NET MVC para gestión de dedicaciones, esfuerzo por proyecto/WP, timesheets y procesos de soporte de gestión (recordatorios, alarmas y tareas batch).

En su estado actual, la solución incorpora:

- Gestión web por roles (Admin, ProjectManager, Researcher, Leader, User).
- Persistencia en SQL Server vía EF Core (`TRSDBContext`).
- Jobs programados con Quartz para procesos periódicos.
- Sistema modular de alarmas por usuario basado en reglas (`IUserAlarmRule`).
- Alarma específica de negocio **Out of Contract con esfuerzo asignado** con vista de revisión y acceso directo a corrección de effort.

---

## 2. Objetivos de negocio

1. **Asegurar calidad del dato operativo** (timesheets, esfuerzo, dedicaciones).
2. **Reducir incidencias de planificación** detectando casos anómalos con alarmas.
3. **Acelerar resolución** conectando cada alerta con una acción directa.
4. **Escalar por rol**: cada tipo de usuario visualiza solo información relevante.
5. **Mantener extensibilidad** para añadir nuevas alarmas sin refactor global.

---

## 3. Alcance funcional actual

### 3.1 Módulos principales

- Home / navegación principal.
- Proyectos (Details, Effort Plan, Personnel Selection, Personnel Effort Plan).
- Personal (calendario, dedicaciones, viajes, logs).
- Timesheet.
- Admin / herramientas de soporte.
- Sistema de alarmas en topbar + endpoint API `/api/alarms/me`.

### 3.2 Alarmas implementadas

1. `timesheet.pending.previous_month`.
2. `project.out_of_contract.assigned_effort`.
3. `timesheet.current_month.no_hours`.

Cada alarma se ejecuta por regla independiente y filtra por roles aplicables.

---

## 4. Arquitectura de alto nivel

### 4.1 Estilo arquitectónico

- **MVC por capas**:
  - Presentación: Controllers + Views Razor.
  - Aplicación/Negocio: Services.
  - Datos: EF Core (`TRSDBContext`) sobre SQL Server.

### 4.2 Componentes técnicos relevantes

- ASP.NET Core MVC.
- Identity (autenticación/autorización por rol).
- Entity Framework Core.
- Quartz.NET para jobs.
- Serilog para logging.
- Bootstrap + Bootstrap Icons para UI.

### 4.3 Composición del sistema de alarmas

- `UserAlarmService`: orquestador.
- `IUserAlarmRule`: contrato común de reglas.
- `UserAlarmContext`: contexto de ejecución (usuario + roles).
- Reglas concretas en `Services/Alarms`.

Este diseño cumple principio OCP (Open/Closed): se agregan nuevas reglas sin alterar el motor central.

---

## 5. Seguridad y control de acceso

### 5.1 Autenticación

Basada en ASP.NET Identity con cookie auth.

### 5.2 Autorización

- Control por roles en controladores y políticas.
- La pantalla de revisión de Out-of-Contract está restringida a `Admin,ProjectManager`.

### 5.3 Alcance de datos por rol (caso Out-of-Contract)

- **Admin**: alcance global de proyectos.
- **ProjectManager**: solo proyectos donde es `PM` o `FM`.

---

## 6. Modelo funcional de recordatorios y alarmas

### 6.1 Recordatorios de timesheet

Comparan horas declaradas frente a umbral requerido del mes anterior con lógica de dedicación/ausencias/asignación.

### 6.2 Alarmas en topbar

- Campana con contador.
- Dropdown de alarmas activas.
- Acción contextual por alarma (“Revisar”).

### 6.3 Flujo lógico de resolución

1. Usuario accede.
2. Se calculan alarmas activas para su rol.
3. Usuario abre alarma.
4. Navega a vista accionable.
5. Corrige en pantalla operativa (effort/timesheet/etc.).

---

## 7. Especificación: alarma Out-of-Contract con esfuerzo asignado

### 7.1 Objetivo

Detectar personas con esfuerzo (`Perseffort.Value > 0`) en mes actual o anterior sin dedicación activa en ese mes.

### 7.2 Reglas de aplicabilidad

- Solo roles: `Admin`, `ProjectManager`.
- Scoping por proyecto según rol (global o PM/FM).

### 7.3 Criterios de detección

Para cada combinación Persona–Proyecto–Mes dentro del alcance:

- Existe esfuerzo asignado en `perseffort`.
- No existe ningún registro en `dedication` que solape ese mes para la persona.

### 7.4 Resultado mostrado al usuario

- Alarma en dropdown con severidad `danger`.
- Acción “Revisar” abre `/AlarmCenter/OutOfContractAssignedEffort`.
- Tabla con:
  - Persona,
  - Proyecto,
  - Mes,
  - Effort asignado,
  - Estado (Out of contract),
  - Acción para corregir.

### 7.5 Acción correctiva directa

Cada fila enlaza a `Projects/GetPersonnelEffortsByPerson/{projId}/{personId}` para editar y resolver incidencia en contexto real.

---

## 8. Diseño de la experiencia de usuario (UX/UI)

### 8.1 Topbar de cuenta y alarmas

- Menú de usuario compactado bajo icono.
- Campana con badge optimizado visualmente.
- Acciones de cuenta centralizadas (`Change Password`, `Logout`).

### 8.2 Vista de revisión Out-of-Contract

- Diseño en cards + tabla responsive.
- Resumen superior de incidencias.
- Estado visual claro mediante badges.
- CTA de revisión/corrección por fila.

---

## 9. Operación y ejecución técnica

### 9.1 Jobs y automatización

Quartz orquesta procesos diarios/semanales (carga de datos y recordatorios).

### 9.2 Logging

Serilog configurado a nivel aplicación para trazabilidad operativa.

### 9.3 Consideraciones de rendimiento

- Filtrado temprano por proyectos en alcance.
- Agregación de esfuerzo por persona/proyecto/mes.
- Consulta de dedicaciones acotada al rango de 2 meses objetivo.

---

## 10. Guía de mantenimiento y extensión

### 10.1 Añadir una nueva alarma

1. Crear clase en `Services/Alarms` implementando `IUserAlarmRule`.
2. Definir roles aplicables y criterios de negocio.
3. Devolver `UserAlarmViewModel` solo si hay incidencia.
4. Registrar la regla en DI (`AddScoped<IUserAlarmRule, ...>()`).
5. Si procede, crear vista accionable y enlazarla vía `ActionUrl`.

### 10.2 Buenas prácticas

- Mantener reglas sin lógica de presentación.
- Reutilizar servicios de consulta para evitar duplicación.
- Documentar cada alarma (objetivo, roles, consultas, acción correctiva).

---

## 11. Riesgos y decisiones abiertas

1. **Falta de compilación en entorno actual de revisión**: no disponible `dotnet` en el contenedor.
2. **Estandarización futura**: parametrización de reglas por `appsettings` o BD.
3. **Trazabilidad funcional**: recomendable incluir telemetría de “alarma vista/alarma resuelta”.
4. **Pruebas automáticas**: recomendable ampliar test unitarios/integración para cada regla crítica.

---

## 12. Checklist corporativo de entrega documental

- [x] Contexto funcional y técnico de la solución.
- [x] Arquitectura de componentes.
- [x] Definición de roles y alcance de datos.
- [x] Especificación detallada de la alarma principal actual.
- [x] Flujo de resolución extremo a extremo.
- [x] Guía de evolución y mantenimiento.

---

## 13. Referencias internas

- `docs/architecture.md`
- `docs/reminders-and-alarms.md`
- Código de alarmas en `Services/Alarms/`
- Vista de revisión en `Views/AlarmCenter/OutOfContractAssignedEffort.cshtml`
