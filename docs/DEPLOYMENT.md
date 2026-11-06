# SmartSchool SaaS: installation and deployment

This guide covers building the packages, configuring the server, and hosting on **IIS (Windows)** or **Nginx (Linux)**.

---

## 1. Requirements

| Component | Version / notes |
|---|---|
| .NET | **.NET 10** SDK to build. **ASP.NET Core 10 Hosting Bundle** (Windows/IIS) or ASP.NET Core 10 Runtime (Linux) on the server |
| Database | Microsoft SQL Server 2019+ or SQL Server Express |
| Web server | IIS 10+ on Windows, or Nginx on Linux (Ubuntu 22.04/24.04 tested) |
| HTTPS | A domain name and a TLS certificate (Let's Encrypt works well) |
| Fonts (Linux only) | `fonts-noto-core` so PDF report cards render Arabic |
| Browser | Current Chrome, Edge or Firefox |

---

## 2. Build the packages

From the repository root:

```bash
dotnet publish src/SmartSchool.Api -c Release -o publish/api
```

`publish/api` is the **all-in-one package**: the REST API plus the Blazor WebAssembly client, served from the same site. This is the recommended setup.

The portable package includes native PDF libraries for every operating system (about 150 MB). Publish for your server only to make it much smaller:

```bash
dotnet publish src/SmartSchool.Api -c Release -r win-x64 --self-contained false -o publish/api     # IIS
dotnet publish src/SmartSchool.Api -c Release -r linux-x64 --self-contained false -o publish/api   # Nginx
```

To host the client separately (for example on a CDN or another domain), also build the standalone client:

```bash
dotnet publish src/SmartSchool.Client -c Release -o publish/client
```

The static site is `publish/client/wwwroot`. Set `ApiBaseUrl` in `wwwroot/appsettings.json` to the API address (e.g. `https://api.your-domain.com/`), and list the client's address in the API's `Cors:AllowedOrigins`.

---

## 3. Database

1. Create an empty database, e.g. `SmartSchoolSaaS`.
2. Create a SQL login for the application and give it **db_owner** on that database (needed for the automatic migrations on start-up). You can lower it to `db_datareader` + `db_datawriter` + `EXECUTE` after the first start if you prefer to run future migrations by hand.
3. Put the connection string in `ConnectionStrings:Default`.

The schema is created and upgraded **automatically when the app starts**. To generate an SQL script instead (for a DBA to review):

```bash
dotnet tool restore
dotnet ef migrations script --idempotent -p src/SmartSchool.Infrastructure -s src/SmartSchool.Api -o publish/database.sql
```

**Backups:** schedule full database backups, and back up the uploads folder (`Storage:RootPath`) at the same time. The two belong together.

---

## 4. Configuration

Copy `src/SmartSchool.Api/appsettings.Production.example.json` to `appsettings.Production.json` next to the published DLLs, **or** set environment variables (double underscore = nesting):

| Setting | Required | Purpose |
|---|---|---|
| `ConnectionStrings__Default` | yes | SQL Server connection |
| `Jwt__SigningKey` | yes | Random secret, 32+ characters. Changing it signs everybody out |
| `Seed__SuperAdminEmail` / `Seed__SuperAdminPassword` | first start | Creates the platform owner account if none exists. Change the password after signing in (My profile → Change password) |
| `Seed__CreateDemoData`, `Seed__ShowDemoAccounts` | **false** in production | Demo tenants and the quick-login buttons |
| `ReverseProxy__Enabled` | true behind IIS/Nginx | Trusts `X-Forwarded-*` so client IPs (audit trail, rate limits) and HTTPS are correct |
| `Storage__RootPath` | optional | Uploaded documents; default `App_Data/uploads` under the app folder |
| `Cors__AllowedOrigins__0` | only for a separate client | Address of the standalone client |
| `Session__IdleTimeoutMinutes` | optional | Sign-out after inactivity (default 60) |
| `Monitoring__*RetentionDays` | optional | How long technical logs and page views are kept |

Generate a signing key, for example:

```bash
openssl rand -base64 48
```

The environment must be `Production` (the default when `ASPNETCORE_ENVIRONMENT` is not set). In Production, Swagger is off, HTTPS redirection and HSTS are on, and demo accounts are hidden.

---

## 5. Hosting on IIS (Windows Server)

1. Install the **ASP.NET Core 10 Hosting Bundle** and restart IIS (`iisreset`).
2. Copy `publish/api` to e.g. `C:\inetpub\smartschool`.
3. In IIS Manager:
   - **Application pool**: *.NET CLR version* = **No Managed Code**, *Identity* = `ApplicationPoolIdentity`.
   - **Site**: physical path = the folder above, binding **https** with your certificate (plus an http → https redirect).
4. Give the app-pool identity **Modify** rights on `App_Data` and `logs`:
   ```powershell
   icacls "C:\inetpub\smartschool\App_Data" /grant "IIS AppPool\SmartSchool:(OI)(CI)M"
   icacls "C:\inetpub\smartschool\logs" /grant "IIS AppPool\SmartSchool:(OI)(CI)M"
   ```
5. Set the configuration (step 4), for example in the generated `web.config`:
   ```xml
   <aspNetCore processPath="dotnet" arguments=".\SmartSchool.Api.dll" hostingModel="inprocess">
     <environmentVariables>
       <environmentVariable name="ReverseProxy__Enabled" value="true" />
       <environmentVariable name="Jwt__SigningKey" value="..." />
     </environmentVariables>
   </aspNetCore>
   ```
6. Browse to `https://your-domain/health`. It should return `"status":"Healthy"`.

Windows already includes Arial/Tahoma, so the Arabic PDFs work without extra fonts.

---

## 6. Hosting on Linux with Nginx

```bash
# runtime and fonts (Ubuntu)
sudo apt-get install -y aspnetcore-runtime-10.0 fonts-noto-core nginx

# app folder
sudo mkdir -p /var/www/smartschool && sudo cp -r publish/api/* /var/www/smartschool/
sudo chown -R www-data:www-data /var/www/smartschool
```

`/etc/systemd/system/smartschool.service`:

```ini
[Unit]
Description=SmartSchool SaaS
After=network.target

[Service]
WorkingDirectory=/var/www/smartschool
ExecStart=/usr/bin/dotnet /var/www/smartschool/SmartSchool.Api.dll --urls http://127.0.0.1:5000
Restart=always
RestartSec=10
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ReverseProxy__Enabled=true
EnvironmentFile=/etc/smartschool.env

[Install]
WantedBy=multi-user.target
```

Put the secrets in `/etc/smartschool.env` (mode 600), e.g. `ConnectionStrings__Default=...` and `Jwt__SigningKey=...`.

`/etc/nginx/sites-available/smartschool`:

```nginx
server {
    listen 80;
    server_name your-domain.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name your-domain.com;
    ssl_certificate     /etc/letsencrypt/live/your-domain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/your-domain.com/privkey.pem;

    client_max_body_size 12m;   # uploads are limited to 10 MB by the app

    location / {
        proxy_pass         http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/smartschool /etc/nginx/sites-enabled/
sudo systemctl enable --now smartschool && sudo nginx -t && sudo systemctl reload nginx
curl -s https://your-domain.com/health
```

---

## 7. After the first start

1. Sign in with the SuperAdmin account and **change its password** (My profile).
2. Review **Plans** (prices, limits, trial days).
3. Create the first **tenant**. It starts on the plan you choose, and you can then add its schools and administrator.
4. The tenant administrator sets up **academic years, subjects, rooms, teachers, classes and students**.

---

## 8. Operations

| Task | Where |
|---|---|
| Health check for uptime monitoring | `GET /health` (database included) |
| Application log files | `logs/smartschool-YYYYMMDD.log` (30 days kept) |
| Warnings/errors in the app | Monitoring → Technical logs (SuperAdmin) |
| Who changed what | Monitoring → Audit trail |
| Active users and sessions | Monitoring → Usage analytics / User sessions |
| Subscription expiry and auto-renewal | Runs hourly in the background; also *Subscriptions → Run renewals* |

**Upgrading:** stop the site, back up the database and uploads, copy the new `publish/api` files over the old ones (keep `appsettings.Production.json`, `App_Data` and `logs`), and start again. Migrations run automatically.

---

## 9. Security checklist

- [ ] HTTPS only, with a valid certificate
- [ ] `Jwt:SigningKey` is long, random and kept out of source control
- [ ] Demo data and demo accounts disabled
- [ ] SuperAdmin password changed after the first sign-in
- [ ] SQL login limited to the SmartSchool database
- [ ] `App_Data/uploads` not served directly by the web server (the app checks permissions on every download)
- [ ] Database and uploads backed up on a schedule, and a restore tested
