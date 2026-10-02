-- Creates the least-privilege login the web containers use, after the migrate
-- container (which connects as sa) has created and migrated the AIVES database.
-- Run by the db-init service in compose.yaml; safe to run on every deploy.
-- sqlcmd reads $(APP_PASSWORD) from the environment variable of the same name.

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'aives_app')
    CREATE LOGIN [aives_app] WITH PASSWORD = N'$(APP_PASSWORD)', CHECK_POLICY = ON;
ELSE
    ALTER LOGIN [aives_app] WITH PASSWORD = N'$(APP_PASSWORD)';
GO

USE [AIVES];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'aives_app')
    CREATE USER [aives_app] FOR LOGIN [aives_app];
GO

-- Read/write data only: no DDL, so schema changes must go through the migrate step.
ALTER ROLE db_datareader ADD MEMBER [aives_app];
ALTER ROLE db_datawriter ADD MEMBER [aives_app];
GO
