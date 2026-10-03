# WhatsApp Messaging Platform

Send customer reports, appointments and updates as approved WhatsApp templates through Meta's
official WhatsApp Cloud API.

- `backend/WaPlatform.Api`: ASP.NET Core (.NET 10) Web API, EF Core, SQL Server
- `frontend`: Next.js 16 (App Router, Tailwind). The browser only talks to Next.js; `/api/*` is
  forwarded to the backend, so the HttpOnly session cookie stays same-origin.

## Status

| Phase | Scope | State |
|---|---|---|
| 1 | Meta setup (business verification, WABA, number, token, 6 templates) | done by you, in parallel |
| 2 | Foundation: login, Admin/Employee roles, Users, audit log, first-admin command | **done** |
| 3 | WhatsApp core: Cloud API client, template sync, media upload, Settings | **in progress** (client, status, test send) |
| 4 | Queue + webhook | **in progress** (webhook receives and stores events) |
| 5 | Customers + single send | |
| 6 | Bulk send (campaigns) | |
| 7 | Dashboard, automatic triggers, hardening | |

## Run locally

Prerequisites: .NET 10 SDK, Node 20+, SQL Server.

1. Set the connection string in `backend/WaPlatform.Api/appsettings.Development.json`
   (currently `localhost\MSSQLSERVER01` with Windows auth).
2. Create the database and the first admin:
   ```
   cd backend/WaPlatform.Api
   dotnet run --launch-profile http -- create-admin --email you@company.com --name "Your Name"
   ```
   (It prompts for the password. `-- migrate` only applies migrations.)
3. Start the API: `dotnet run --launch-profile http` (http://localhost:5075)
4. Start the web app: `cd frontend && npm install && npm run dev`, then open http://localhost:3000

Set `API_URL` for the frontend if the backend runs elsewhere (production builds default to
`https://whatsappapi.alamalhospitaljo.com`).

## Security in place (phase 2)

- bcrypt password hashes (work factor 12), min 8 chars with letters and numbers
- HttpOnly, SameSite=Strict session cookie; 30 min sliding timeout; `Secure` outside development
- Lockout after 5 failed logins for 15 minutes (admins can unlock); login rate limit of 10/min per IP
- Every request re-checks the user: disabling a user, changing their role or resetting their
  password ends their sessions immediately
- Roles enforced on the server: `/api/users` and `/api/audit` return 403 to employees
- Users created or reset by an admin must change their password before any other API call
- Audit log of logins, failures, lockouts, password changes and every user change, with IP

Settings in `appsettings.json` under `Auth`: `SessionMinutes`, `MaxFailedLogins`, `LockoutMinutes`.

## WhatsApp configuration

Non-secret settings live in `appsettings.json` under `WhatsApp`. Secrets never go there:

- Locally: `backend/WaPlatform.Api/appsettings.Local.json` (git-ignored, excluded from publish output).
- Production: environment variables, which override everything else.

| Setting | Env var | Where to get it |
|---|---|---|
| `AccessToken` | `WhatsApp__AccessToken` | Business Settings > System users > Generate token (`whatsapp_business_messaging`, `whatsapp_business_management`). The API Setup page token expires after 24 h. |
| `AppSecret` | `WhatsApp__AppSecret` | App dashboard > App settings > Basic > App secret |
| `VerifyToken` | `WhatsApp__VerifyToken` | Any random string you choose (`openssl rand -hex 24`); type the same value into the dashboard |
| `PhoneNumberId` | `WhatsApp__PhoneNumberId` | WhatsApp > API Setup |
| `BusinessAccountId` | `WhatsApp__BusinessAccountId` | WhatsApp > API Setup |

### Webhook

- Callback URL: `https://<public host>/api/webhooks/whatsapp` (through the Next.js host or straight to the API).
  Must be public HTTPS; for local testing use a tunnel, e.g. `cloudflared tunnel --url http://localhost:5075`.
- `GET` answers Meta's verification with the verify token; `POST` rejects any request whose
  `X-Hub-Signature-256` doesn't match the app secret, then stores the raw payload in `webhook_events`.
- After verifying, subscribe to the `messages` field. Production events only arrive once the app is published.

### Admin endpoints

- `GET /api/whatsapp/status`: missing settings and the phone number as Meta sees it (checks the token).
- `POST /api/whatsapp/test-message` `{ "to": "9665XXXXXXXX" }`: sends the `hello_world` template.
  With a test number the recipient must be added under "To" on the API Setup page.

### Public pages (for the Meta app settings)

- Privacy Policy URL: `https://whatsappapi.alamalhospitaljo.com/privacy` (English + Arabic; `#data-deletion` covers deletion requests)
- Terms of Service URL: `https://whatsappapi.alamalhospitaljo.com/terms`

Source: `backend/WaPlatform.Api/Legal/*.html`. The policy promises 6-month retention; `RetentionService`
deletes webhook events older than `WhatsApp:RetentionMonths` (default 6) daily. Keep the two in sync.

## Deployment

| Part | URL | Host | How it deploys |
|---|---|---|---|
| Frontend | https://whatsapp.alamalhospitaljo.com | Hostinger (Node.js app) | Push to `main` → `Deploy frontend` workflow lints/builds, then moves the `deploy/frontend` branch to that commit → Hostinger builds and runs it |
| API | https://whatsappapi.alamalhospitaljo.com | site4now (IIS) | Push to `main` touching `backend/` → `Deploy API` workflow builds, migrates the DB, uploads over FTPS, health-checks |

`CI` builds and lints both apps on every PR and push.

### Hostinger (frontend)

Connect the GitHub repo, branch **`deploy/frontend`**, root directory **`frontend`**, framework Next.js, Node 22,
build `npm run build`, start `npm start`. Environment variables:

- `PROXY_SECRET`: same value as the API's `Proxy__Secret`. Lets the API see each visitor's real IP
  (audit log, login rate limit). Without it everything still works, but all users share the
  Hostinger server's IP.
- `API_URL` (optional): only to point at a different API.

### API secrets

GitHub repository secrets used by `Deploy API`: `API_DB_CONNECTION`, `FTP_HOST`, `FTP_USER`,
`FTP_PASSWORD`, `FTP_DIR`. Runtime secrets live only in the server's `web.config` as environment
variables (`ConnectionStrings__Default`, `WhatsApp__*`, `Proxy__Secret`); the workflow never
uploads a `web.config`, so they are not overwritten. The repository is public: never commit secrets.

CORS allows `https://whatsapp.alamalhospitaljo.com` (`Cors:AllowedOrigins`), though the browser
normally reaches the API through the frontend's `/api` proxy.
