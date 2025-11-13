# TRS – Arquitectura y Flujos

> Documento de referencia arquitectónica para el sistema **TRS** (ASP.NET MVC, C#, EF Core, SQL Server).
>
> Cómo visualizar este documento en **Visual Studio 2022**: instala la extensión **“Markdown Editor (yuml / mermaid support)”** de Mads Kristensen y abre la vista previa de Markdown.

---

## Índice
1. [Visión General](#visión-general)
2. [C4 – Nivel Contexto](#c4--nivel-contexto)
3. [C4 – Nivel Contenedores](#c4--nivel-contenedores)
4. [Flujo de Datos Global (DFD)](#flujo-de-datos-global-dfd)
5. [UML de Clases (Modelo EF Core)](#uml-de-clases-modelo-ef-core)
6. [Diagramas de Secuencia (Procesos Clave)](#diagramas-de-secuencia-procesos-clave)
   - [Recordatorio de Timesheets](#recordatorio-de-timesheets)
   - [Carga y Procesado de Liquidaciones](#carga-y-procesado-de-liquidaciones)
   - [Ajuste Automático de Overload](#ajuste-automático-de-overload)
7. [Mapa de Dependencias (Controllers → Services → Datos)](#mapa-de-dependencias-controllers--services--datos)
8. [Cómo renderizar y exportar](#cómo-renderizar-y-exportar)

---

## Visión General
TRS es una aplicación **ASP.NET MVC** que gestiona dedicaciones, esfuerzos (PMs), ausencias y carga de datos externos. Los usuarios (Investigadores, Project Managers, Admin) interactúan mediante vistas Razor, mientras que la lógica de negocio se concentra en **Services** y el acceso a datos en **EF Core** a través de `TRSDBContext`. Procesos automáticos programados con **Quartz.NET** ejecutan sincronizaciones y recordatorios.

---

## C4 – Nivel Contexto
```mermaid
C4Context
    title Sistema TRS - Contexto General

    Person(user, "Empleado / Investigador", "Reporta horas y consulta su calendario")
    Person(pm, "Project Manager / Líder", "Supervisa efforts y asignaciones")
    Person(admin, "Administrador", "Gestiona usuarios, contratos, bloqueos y procesos")

    System(trs, "TRS WebApp", "ASP.NET MVC (C#, EF Core, SQL Server)")
    SystemDb(db, "SQL Server", "Base de datos de TRS")
    SystemExt(woffu, "Woffu API", "Origen de ausencias")
    SystemExt(mail, "SMTP / Email", "Envío de recordatorios y notificaciones")

    Rel(user, trs, "Uso vía navegador web")
    Rel(pm, trs, "Dashboards y reportes")
    Rel(admin, trs, "Configuración y tareas automáticas")

    Rel(trs, db, "Lectura/Escritura de datos de dominio")
    Rel(trs, woffu, "Importa ausencias y diarios")
    Rel(trs, mail, "Envía correos de recordatorio")
```

---

## C4 – Nivel Contenedores
```mermaid
C4Container
    title Sistema TRS - Nivel de Contenedores

    System_Boundary(trs, "TRS Web Application") {
        Container(webapp, "ASP.NET MVC App", "C# / Razor / EF Core", "Gestión de timesheets, proyectos y esfuerzo")
        ContainerDb(database, "SQL Server (TRSDBContext)", "T-SQL / EF Core", "Persistencia de entidades")
        Container(scheduler, "Quartz.NET Jobs", "C#", "Carga diaria, recordatorios, ajustes")
    }

    Person(user, "Empleado")
    Person(pm, "Project Manager")
    System_Ext(woffu, "Woffu API", "Ausencias")
    System_Ext(mail, "SMTP Server", "Notificaciones")

    Rel(user, webapp, "Interacción diaria")
    Rel(pm, webapp, "Supervisión / Reportes")
    Rel(webapp, database, "CRUD EF Core")
    Rel(webapp, scheduler, "Programa y lanza jobs")
    Rel(scheduler, database, "Batch R/W")
    Rel(scheduler, woffu, "Sincroniza ausencias")
    Rel(scheduler, mail, "Envía recordatorios")
```

---

## Flujo de Datos Global (DFD)
```mermaid
graph TD

    subgraph External Systems
        A[Woffu API]
        B[SMTP Server]
    end

    subgraph TRS Application
        C[Controllers (MVC)]
        D[Services (WorkCalendar, Reminder, LoadData, ...)]
        E[TRSDBContext]
    end

    subgraph Database
        F[(SQL Server - TRS)]
    end

    A --> D
    C --> D
    D --> E
    E --> F
    D --> B
```

**Resumen del flujo:**
1. Los **Controllers** reciben Peticiones HTTP y delegan en **Services**.
2. Los **Services** implementan reglas de negocio y acceden al **DbContext**.
3. **Quartz.NET** ejecuta procesos automáticos con el mismo pipeline.
4. **Woffu** aporta ausencias; **SMTP** recibe las notificaciones.

---

## UML de Clases (Modelo EF Core)
```mermaid
classDiagram
    direction LR

    class Personnel {
      +int Id
      +string Name
      +string Surname
      +string Email
      +DateTime StartDate
      +DateTime EndDate
    }

    class Dedication {
      +int Id
      +int PersId
      +DateTime Start
      +DateTime End
      +decimal Reduc
      +int Type
    }

    class Project {
      +int ProjId
      +string Acronim
      +string SapCode
      +DateTime Start
      +DateTime EndReportDate
    }

    class Wp {
      +int Id
      +int ProjId
      +string Name
      +decimal Pms
      +DateTime StartDate
      +DateTime EndDate
    }

    class Wpxperson {
      +int Id
      +int Wp
      +int Person
    }

    class Projectxperson {
      +int Id
      +int ProjId
      +int Person
    }

    class Timesheet {
      +int Id
      +int WpxPersonId
      +DateTime Day
      +decimal Hours
    }

    class Perseffort {
      +int Id
      +int WpxPerson
      +DateTime Month
      +decimal Value
    }

    class PersMonthEffort {
      +int Id
      +int PersonId
      +DateTime Month
      +decimal Value
    }

    class Leave {
      +int Id
      +int PersonId
      +DateTime Day
      +int Type
      +decimal LeaveReduction
    }

    class AffxPerson {
      +int Id
      +int PersonId
      +int LineId
      +int AffId
      +DateTime Start
      +DateTime End
    }

    class AffHours {
      +int Id
      +int AffId
      +DateTime StartDate
      +DateTime EndDate
      +decimal Hours
    }

    class DailyPMValue {
      +int Id
      +int Year
      +int Month
      +decimal PmPerDay
    }

    class ProjectMonthLock {
      +int Id
      +int ProjId
      +int Year
      +int Month
      +bool IsLocked
    }

    class Liquidation {
      +string Id
      +int PersId
      +string Project1
      +decimal Dedication1
      +string Project2
      +decimal Dedication2
      +DateTime Start
      +DateTime End
      +string Status
    }

    class Liqdayxproject {
      +int Id
      +string LiqId
      +int PersId
      +int ProjId
      +DateTime Day
      +decimal Dedication
      +decimal PMs
      +string Status
    }

    class Leader {
      +int Id
      +int LeaderId
      +int PersonId
      +string Type
    }

    Personnel "1" --> "*" Dedication
    Personnel "1" --> "*" Leave
    Personnel "1" --> "*" AffxPerson
    Personnel "1" --> "*" Projectxperson
    Personnel "1" --> "*" Wpxperson
    Personnel "1" --> "*" PersMonthEffort
    Personnel "1" --> "*" Timesheet

    Project "1" --> "*" Wp
    Project "1" --> "*" Projectxperson
    Project "1" --> "*" ProjectMonthLock

    Wp "1" --> "*" Wpxperson
    Wpxperson "1" --> "*" Timesheet
    Wpxperson "1" --> "*" Perseffort

    AffxPerson "*" --> "1" AffHours

    Liquidation "1" --> "*" Liqdayxproject
    Project "1" --> "*" Liqdayxproject
    Personnel "1" --> "*" Liqdayxproject

    DailyPMValue ..> Timesheet
    ProjectMonthLock ..> Perseffort
```

> *Nota:* Ajusta nombres o campos si tu `TRSDBContext` difiere en alguna entidad.

---

## Diagramas de Secuencia (Procesos Clave)

### Recordatorio de Timesheets
```mermaid
sequenceDiagram
    autonumber
    participant Quartz as Quartz.NET
    participant Reminder as ReminderService
    participant WorkCal as WorkCalendarService
    participant Db as TRSDBContext
    participant SMTP as EmailSender/SMTP

    Quartz->>Reminder: SendTimesheetRemindersAsync(firstWeek)
    Reminder->>Db: Obtener Personnel con email
    loop por persona
        Reminder->>WorkCal: GetDeclaredHoursPerMonthForPerson()
        Reminder->>WorkCal: CalculateMonthlyPM()
        alt firstWeek == true
            Reminder->>SMTP: Email general (meses pendientes)
        else stillIncomplete
            Reminder->>SMTP: Email focalizado (mes anterior)
        end
    end
```

### Carga y Procesado de Liquidaciones
```mermaid
sequenceDiagram
    autonumber
    participant Admin as Admin/Job
    participant Load as LoadDataService
    participant WorkCal as WorkCalendarService
    participant Db as TRSDBContext

    Admin->>Load: LoadLiquidationsFromFileAsync()
    Load->>Db: Upsert Liquidation
    Admin->>Load: ProcessLiquidationsAsync()
    Load->>Db: Obtener liquidaciones activas
    loop por liquidación
        Load->>Db: Borrar Liqdayxproject previos
        Load->>WorkCal: CalculateDailyPM(fecha)
        Load->>Db: Crear Liqdayxproject (día/proyecto)
        alt Falta WP TRAVELS
            Load->>Db: Crear WP TRAVELS + Wpxperson
        end
        Load->>Db: Upsert Perseffort(TRAVELS)
        Load->>Db: Liquidation.Status = 3
    end
```

### Ajuste Automático de Overload
```mermaid
sequenceDiagram
    autonumber
    participant Quartz as Quartz.NET
    participant WorkCal as WorkCalendarService
    participant Db as TRSDBContext

    Quartz->>WorkCal: Ajuste mensual (personId, month)
    WorkCal->>Db: Obtener Persefforts del mes
    WorkCal->>Db: Obtener PersMonthEffort
    WorkCal->>Db: Consultar ProjectMonthLock / viajes
    alt Effort > PM permitido
        WorkCal->>WorkCal: Reducción proporcional por proyecto/WP
        WorkCal->>Db: Actualizar Persefforts
    else
        WorkCal->>Db: Sin cambios
    end
```

---

## Mapa de Dependencias (Controllers → Services → Datos)
```mermaid
flowchart LR
    subgraph Controllers
        A[TimesheetController]
        B[LeaderController]
        C[AdminController]
        D[ProjectManagerController]
        E[AccountController]
        F[HomeController]
        G[WpsController]
        H[PersonnelsController]
        I[ToolsController]
        J[ErrorController]
    end

    subgraph Services
        S1[WorkCalendarService]
        S2[LoadDataService]
        S3[ReminderService]
        S4[RoleService]
        S5[EmailSender]
    end

    subgraph Data
        D1[(TRSDBContext)]
        D2[(SMTP)]
    end

    A --> S1
    A --> D1
    B --> S1
    B --> D1
    C --> S1
    C --> S2
    C --> S3
    C --> S4
    C --> D1
    D --> S1
    D --> D1
    E --> D1
    F --> D1
    G --> S1
    G --> D1
    H --> D1
    I --> S1
    I --> D1
    J --> D1

    S1 --> D1
    S2 --> D1
    S3 --> S1
    S3 --> D1
    S3 --> D2
    S4 --> D1
    S5 --> D2
```

---

## Cómo renderizar y exportar
- **Visual Studio 2022**: instala la extensión **“Markdown Editor (yuml / mermaid support)”** y abre la vista previa del `.md`.
- **GitHub/GitLab/Azure DevOps**: sube este `ARCHITECTURE.md`; renderizan Mermaid nativamente.
- **Mermaid Live**: copia cualquier bloque en [https://mermaid.live](https://mermaid.live) y usa *Export → PNG/SVG*.

> Sugerencia: guarda también versiones PNG/SVG en `docs/` si vas a incluirlas en presentaciones o PDFs.

