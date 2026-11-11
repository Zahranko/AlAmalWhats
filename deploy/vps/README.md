# Moving the API from site4now (SmarterASP) to the VPS

The API moves to the Hostinger VPS (`187.126.1.126`) next to AlAmalBusiness, in Docker, deployed
blue/green by `nginny-deploy` like the other apps there. **The address stays
`https://whatsappapi.alamalhospitaljo.com`**, so the frontend, Meta's webhook, the API keys of other
systems and the `/chat` template buttons need no change. The frontend stays on Hostinger.

| | site4now (old) | VPS (new) |
|---|---|---|
| Runs as | IIS app, FTPS upload | container `whatsapp-api-blue/green` on `127.0.0.1:5131/5132`, nginx in front |
| Database | site4now SQL Server | `WaPlatform` on the VPS's SQL Server container (`mssql,1433`), nightly `mssql-backup` |
| Migrations | pipeline runs `migrate` | the new container applies them before it reports ready (`Database__MigrateOnStartup`) |
| Secrets | `web.config` env vars | `/opt/apps/whatsapp-api/app.env` (root, 600) |
| Keep-alive | needed (IIS idles out) | not needed |

Files here: `whatsapp-api/app.conf` → `/opt/apps/whatsapp-api/app.conf`,
`whatsapp-api/nginx-site.conf` → `/etc/nginx/sites-available/whatsapp-api`.
(Also copy them into the nginny project, `D:\nginny\deploy\apps\whatsapp-api`, with the others.)

## 1. Before the move (no downtime)

**a. Deploy key** (on this PC):
```
ssh-keygen -t ed25519 -N "" -C whatsapp-github-deploy -f ~/.ssh/whatsapp_github_deploy
```
On the VPS, as root, append the `.pub` to `deployer`'s keys, tied to this app only:
```
echo 'restrict,command="/usr/local/bin/nginny-deploy-gate whatsapp-api" ssh-ed25519 AAAA... whatsapp-github-deploy' \
  >> /home/deployer/.ssh/authorized_keys
```
GitHub → AlAmalWhats → Settings → Secrets → Actions: `VPS_DEPLOY_KEY` = the whole private key file.

**b. App folder** (VPS, root):
```
install -d -m 700 /opt/apps/whatsapp-api
install -m 600 app.conf /opt/apps/whatsapp-api/app.conf
```

**c. DNS**: lower the TTL of `whatsappapi.alamalhospitaljo.com` to 300 s a day ahead.

**d. Image**: merge to `main`; `Deploy API to VPS` builds and pushes the image (it does not deploy
yet, the `VPS_DEPLOY` variable isn't set).

## 2. Cutover (~15-30 min of API downtime)

Meta retries webhook deliveries for days, so incoming messages and delivery statuses that arrive
while the API is down are delivered later, not lost.

1. **Stop the old API** so nothing writes to its database any more: upload `app_offline.htm`
   to the site4now site folder (same as the old deploy does). Disable the `Deploy API` and
   `Keep API awake` workflows (Actions → workflow → ⋯ → Disable).
2. **Back up** the site4now database (control panel → Databases → Backup) and download the `.bak`.
3. **Restore it on the VPS**: copy it to `/srv/mssql/backup/wa.bak`
   (`scp wa.bak root@187.126.1.126:/srv/mssql/backup/ && chown 10001:10001 /srv/mssql/backup/wa.bak`),
   then edit the password in `restore.sql` and run it:
   ```
   source /opt/mssql/sa.env
   docker exec -i -e SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b < restore.sql
   ```
   (If the restore complains about logical file names, run `RESTORE FILELISTONLY FROM DISK = '/var/opt/mssql/backup/wa.bak'`
   and put those names in the `MOVE` lines.)
4. **Secrets**: create `/opt/apps/whatsapp-api/app.env` (root, `chmod 600`) with the values from
   site4now's `web.config`, but the VPS connection string:
   ```
   ConnectionStrings__Default=Server=mssql,1433;Database=WaPlatform;User Id=wa_app;Password=<from restore.sql>;Encrypt=True;TrustServerCertificate=True
   Proxy__Secret=<same as the frontend's PROXY_SECRET>
   WhatsApp__AccessToken=...
   WhatsApp__AppSecret=...
   WhatsApp__VerifyToken=...
   WhatsApp__PhoneNumberId=...
   WhatsApp__BusinessAccountId=...
   ```
   Add `Auth__JwtKey=...` only if web.config has it (otherwise the key stored in the database is used;
   it's encrypted with Data Protection keys that are in the database too, so it carries over).
5. **Start it**: GitHub → Actions → `Deploy API to VPS` → Run workflow. The deploy step must end
   with `done: whatsapp-api is live on blue` (the public check after it fails until DNS moves; ignore that once).
   On the VPS: `curl -s 127.0.0.1:5131/api/health/ready` → `{"status":"ready"}`.
6. **DNS**: point the `A` record of `whatsappapi.alamalhospitaljo.com` to `187.126.1.126`
   (remove any `AAAA` record). Wait until `nslookup whatsappapi.alamalhospitaljo.com 8.8.8.8` shows it.
7. **Certificate + site** (VPS, root):
   ```
   certbot certonly --webroot -w /var/www/letsencrypt -d whatsappapi.alamalhospitaljo.com
   install -m 644 nginx-site.conf /etc/nginx/sites-available/whatsapp-api
   ln -s /etc/nginx/sites-available/whatsapp-api /etc/nginx/sites-enabled/whatsapp-api
   nginx -t && systemctl reload nginx
   ```
   (Port 80 already answers ACME challenges for any name through the default nginny site; if
   certbot fails, enable the site with only its port-80 block first, then add the 443 block.)
8. **Check**:
   - `https://whatsappapi.alamalhospitaljo.com/api/health/ready` → ready
   - sign in at https://whatsapp.alamalhospitaljo.com, open the inbox, send a test message
   - Meta App dashboard → WhatsApp → Configuration → Webhook: *Verify and save* (same URL and token)
   - `GET /api/whatsapp/status` (admin) shows no missing settings
9. GitHub → Settings → Variables → Actions: `VPS_DEPLOY` = `true`, so pushes deploy.

**Rolling back the cutover**: point DNS back to site4now and delete `app_offline.htm` there. Messages
sent from the VPS in between exist only in the VPS database.

## 3. After a week on the VPS

- Remove `deploy-api.yml` and `keepalive.yml`, the FTP_* and API_DB_CONNECTION secrets, and the
  site4now site and database.
- Frontend: set `API_KEEPALIVE=off` on Hostinger (the API no longer idles out).
