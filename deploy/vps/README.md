# API on the VPS

The API runs on the Hostinger VPS (`187.126.1.126`) next to AlAmalBusiness, in Docker, behind nginx
at `https://whatsappapi.alamalhospitaljo.com`. Releases are blue/green through `nginny-deploy`, like
the other apps there. The frontend stays on Hostinger's Node.js hosting.

| | |
|---|---|
| Containers | `whatsapp-api-blue` / `whatsapp-api-green` on `127.0.0.1:5131` / `5132`; nginx upstream `whatsapp_api` |
| Database | `WaPlatform` on the VPS's SQL Server container (`mssql,1433`), login `wa_app` (db_owner of this database only); nightly `mssql-backup` |
| Migrations | the new container applies them before it reports ready (`Database__MigrateOnStartup`, set in the image) |
| Secrets | `/opt/apps/whatsapp-api/app.env` (root, 600); the pipeline never sends them |
| Health | `/api/health` (process), `/api/health/ready` (plus the database), which the deploy waits for |
| Certificate | Let's Encrypt, webroot mode, renewed by certbot's snap timer |

Files here, as installed on the server:

- `whatsapp-api/app.conf` → `/opt/apps/whatsapp-api/app.conf`
- `whatsapp-api/nginx-site.conf` → `/etc/nginx/sites-available/whatsapp-api` (enabled in `sites-enabled`)

They are also kept with the other apps in the nginny project (`D:\nginny\deploy\apps`).

## Deploying

A push to `main` touching `backend/` runs **Deploy API to VPS**: it builds
`ghcr.io/zahranko/alamalwhats-api`, then deploys that digest over SSH as `deployer`. The key
(`VPS_DEPLOY_KEY`, `~/.ssh/whatsapp_github_deploy` on the IT PC) can only run
`deploy|rollback|status whatsapp-api`. Pushes deploy while the repository variable `VPS_DEPLOY` is
`true`; *Run workflow* deploys regardless.

```
ssh -i ~/.ssh/whatsapp_github_deploy deployer@187.126.1.126 "status whatsapp-api"
ssh -i ~/.ssh/whatsapp_github_deploy deployer@187.126.1.126 "rollback whatsapp-api"
```

Rollback restarts the previous image. It does not undo migrations, so keep them additive.

## Changing a secret

As root on the VPS, edit `/opt/apps/whatsapp-api/app.env` (`KEY=value` per line, no quotes), then
redeploy (*Run workflow*) so a fresh container reads it. Keys allowed: see `ENV_KEYS` in `app.conf`.

## Logs

```
docker logs --tail 100 whatsapp-api-$(cat /opt/apps/whatsapp-api/active)
tail -f /var/log/nginx/whatsapp-api.access.log
```
