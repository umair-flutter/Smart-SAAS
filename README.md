# SmartSchool SaaS

Multi-tenant school management platform: ASP.NET Core 10 REST API + Blazor WebAssembly client + EF Core + SQL Server.

## Solution layout

| Project | Purpose |
|---|---|
| `src/SmartSchool.Domain` | Entities (`Tenant`, `School`, `AppUser`, `Role`, `UserSession`…) and ownership interfaces (`ITenantOwned`, `ISchoolOwned`) |
| `src/SmartSchool.Shared` | Contracts shared by API and client: DTOs, enums, permission catalog, claim/header names |
| `src/SmartSchool.Application` | Business services, validation rules, default role permissions |
| `src/SmartSchool.Infrastructure` | `AppDbContext` with tenant/school query filters, migrations, JWT + password hashing, seeder |
| `src/SmartSchool.Api` | REST controllers, JWT auth, permission policies, session validation, Serilog, Swagger, health check; also hosts the client |
| `src/SmartSchool.Client` | Blazor WebAssembly admin UI (Tabler) |

## Run locally

Requirements: .NET 10 SDK, SQL Server Express (`.\SQLEXPRESS`). Change `ConnectionStrings:Default` in `src/SmartSchool.Api/appsettings.json` if your server is different.

```bash
dotnet run --project src/SmartSchool.Api --launch-profile http
```

Open http://localhost:5021. On first start the database is created, migrated and seeded.

- Swagger: http://localhost:5021/swagger (Development only)
- Health: http://localhost:5021/health

### Demo accounts (Development only)

The sign-in page shows quick-login buttons. Passwords are set in `src/SmartSchool.Api/appsettings.Development.json` (`Seed:SuperAdminPassword`, `Seed:DemoPassword`).

| Role | Email |
|---|---|
| SuperAdmin | superadmin@smartschool.local |
| Tenant Administrator | admin@demo-academy.local |
| School Administrator | schooladmin@demo-academy.local |
| Teacher | teacher@demo-academy.local |
| Parent / Guardian | parent@demo-academy.local |
| Student | student@demo-academy.local |
| Second tenant admin (trial) | admin@greenvalley.local |
| Suspended tenant admin (sign-in blocked) | admin@riverside.local |

## How isolation works

- Every request carries a JWT with the user's role, tenant, assigned schools and permissions.
- `AppDbContext` applies global query filters: soft delete, tenant (`TenantId`), and the active school (`X-School-Id` header) for school-owned data.
- Only a SuperAdmin may narrow to a tenant with the `X-Tenant-Id` header; tenant users are pinned to the tenant in their token.
- School-scoped roles (SchoolAdmin, Teacher, Parent, Student) can only select their assigned schools.
- Each endpoint requires a module permission (`[HasPermission(...)]`); the client only hides what the API already forbids.
- Sessions are stored server-side: logout, admin revocation, idle timeout (`Session:IdleTimeoutMinutes`) and suspended subscriptions all invalidate the token within 30 seconds.

## School data

- All school records (years, classes, students…) belong to one school and are filtered by the school picked in the top bar.
- Adding records requires a selected school; with "All schools" selected, lists show every school the user can see.
- The top bar also offers an academic-year switcher (`X-Academic-Year-Id` header). Without a choice, each school's **current** year is used.
- A student has one enrolment per academic year; moving class updates it instead of creating a second one.
- Teacher, student and parent records can create a linked sign-in account with the matching role and school.

## Academics and who sees what

| Role | Sees | Can change |
|---|---|---|
| School admin and above | Everything in the school | Everything; validates attendance; publishes report cards |
| Teacher | Classes they teach (or are homeroom teacher of) | Attendance of those classes until validated; homework, exams and comments for **their own subjects** only; general comment if homeroom teacher |
| Parent | Their linked children only | Justify an absence (reason, note, document) |
| Student | Themselves only | Hand in homework (text and/or file) |

- Exam results and report cards stay hidden from families until published.
- **Averages:** each exam is converted to /20. A subject average is weighted by exam weight, and the overall average by subject coefficient. Absences are left out, and equal averages share a rank.
- **Uploads** are limited to 10 MB of PDF, image, Office, text or ZIP files. They're stored under `App_Data/uploads` (`Storage:RootPath`), and each download is checked against the record the file belongs to.
- **PDF fonts:** on Windows the PDF uses Arial or Tahoma. On Linux, install `fonts-noto-core` so Arabic renders.

## Billing model

- A **plan** sets the price (monthly/yearly), trial length and the maximum schools and users.
- A **subscription** is one period of a tenant on a plan. The tenant keeps its full history; only one period runs at a time.
- The tenant row caches the current status and end date, so every request can check access without extra queries.
- An hourly job (`Billing:MaintenanceIntervalMinutes`) expires finished periods and auto-renews paid ones, issuing their invoice. SuperAdmins can also run it from the Subscriptions page.
- **Payments** are recorded manually against invoices (bank transfer, card, cash, cheque). No payment gateway is connected.
- Tenant administrators see their own plan, usage, balance, invoices and payments read-only.

## Database migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> -p src/SmartSchool.Infrastructure -s src/SmartSchool.Api -o Persistence/Migrations
```

Migrations are applied automatically at startup.

## Production settings

Set these through environment variables or a secret store, never in source control:

- `Jwt__SigningKey` – random secret, 32+ characters
- `Seed__SuperAdminPassword` – first SuperAdmin password (used only when no SuperAdmin exists)
- `ConnectionStrings__Default`
- Keep `Seed__CreateDemoData` and `Seed__ShowDemoAccounts` set to `false`.

## Build phases

1. **Done:** solution, database, multi-tenancy, JWT auth and sessions, roles and permissions, tenants, schools, users, roles UI, sessions monitor, dashboard
2. **Done:** plans with school/user limits, subscriptions (trial, paid, renew, suspend, reactivate, cancel, hourly expiry and auto-renewal job), invoices with lines and tax, manually recorded payments, printable invoices, billing dashboard
3. **Done:** academic years and terms (with a year switcher in the top bar), subjects, rooms, teachers, students, parents/guardians with optional sign-in accounts, classes, enrolments (enrol, move, withdraw), teacher–subject assignments, weekly timetable with class/teacher/room clash detection
4. **Done:** daily attendance with absence reasons, supporting documents and validation; homework with attachments, student hand-ins, late tracking and grading; exams with documents, results and publishing; report cards (weighted /20 averages, rank, attendance, teacher comments, publishing) with PDF export in English, French and Arabic
5. **Done:**
   - Notifications: a notification bell, an inbox, targeted messages (class families, roles, platform), per-person read status, and automatic notifications for new homework, published results, report cards, absences and invoices.
   - Monitoring: an audit trail with field-level changes and a timeline, sign-in auditing, technical logs (warnings and errors stored in the database), and usage analytics.
   - Settings and accounts: custom roles, configurable status options, and a My profile page (language, password).
   - Languages: the full English/French/Arabic interface with right-to-left layout.
   - Delivery: a deployment guide, published packages, and browser tests.

## Languages

The interface is available in English, French and Arabic. The language can be picked on the sign-in page or in the user menu, and it is saved on the account. Arabic switches the whole layout to right-to-left using Tabler's RTL stylesheet. Translations live in `src/SmartSchool.Client/wwwroot/i18n/{fr,ar}.json`, and the key is the English text, so a missing entry simply shows English. Messages that come from the server (validation errors) are still in English.

## Tests

Browser tests (xUnit + Playwright) drive the installed Microsoft Edge against a running app with demo data:

```bash
dotnet run --project src/SmartSchool.Api --launch-profile http
dotnet test tests/SmartSchool.E2E
```

Settings: `E2E_BASE_URL` (default `http://localhost:5021`), `E2E_BROWSER_CHANNEL` (`msedge` by default, or `chrome`), and `E2E_HEADED=1` to watch the browser.

## Deployment

See **[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)**. It covers building the packages (`publish/api`, `publish/client`, `publish/database.sql`), the configuration keys, IIS, Nginx/systemd, backups and a security checklist.
