-- Restores the site4now backup as WaPlatform and creates the app's login.
-- Run as sa inside the mssql container (see ../README.md, cutover step 3).
-- Replace CHANGE_ME with a strong password (openssl rand -base64 32 | tr -dc A-Za-z0-9 | head -c 30; add aA1).
SET NOCOUNT ON;

RESTORE DATABASE WaPlatform
  FROM DISK = N'/var/opt/mssql/backup/wa.bak'
  WITH MOVE N'WaPlatform'     TO N'/var/opt/mssql/data/WaPlatform.mdf',
       MOVE N'WaPlatform_log' TO N'/var/opt/mssql/data/WaPlatform_log.ldf',
       RECOVERY, STATS = 10;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'wa_app')
  CREATE LOGIN wa_app WITH PASSWORD = N'CHANGE_ME', CHECK_POLICY = ON, DEFAULT_DATABASE = WaPlatform;
GO

USE WaPlatform;
GO
-- Drop the users that came with the backup (site4now logins don't exist here).
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'DROP USER ' + QUOTENAME(name) + N';'
FROM sys.database_principals
WHERE type IN ('S', 'U') AND principal_id > 4 AND name <> N'wa_app' AND name NOT LIKE N'##%';
EXEC (@sql);

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'wa_app')
  CREATE USER wa_app FOR LOGIN wa_app;
-- db_owner of this database only: the app applies its own EF migrations on startup.
ALTER ROLE db_owner ADD MEMBER wa_app;
GO

SELECT name, state_desc, recovery_model_desc FROM sys.databases WHERE name = N'WaPlatform';
SELECT COUNT(*) AS users FROM dbo.users;
GO
