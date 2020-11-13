# MySql Backup Agent

This is intended to be a user friendly, easily deployable, containerized service that can perform backups of multiple MySQL/MariaDB databases on individual cron-style schedules.  Has a web interface built on ASP.NET Core 3.1 using server-side Blazor.

Only works for MySQL compatible databases, as it uses the `MySqlBackup.Net` project, which in turn works with `MySql.Data`.

Currently this project is in an early proof of concept stage, and has the following capabilites:

* Cron-style scheduling of backups
* Multiple database targets, each with their own schedule
* Can check the last time the database was updated and skip the backup if it isn't necessary (needs MySQL 5.7.2 or later)
* Compresses the backup with gzip 
* Uploads the backup to an S3 compatible storage

## Intended Purpose
This is meant to be an easy, accessible backup solution for small teams running multiple, moderately sized databases. It has no externally stored data model, all information is passed to it through the configuration file `appsettings.json`.  Its only external dependencies are the databases it backs up, and the storage backend it uses to put the files.

This is intended to allow it to be put up easily and to run in a degraded environment, especially one which is being restored.

## Planned Features

The following features are planned:

* Authentication
    * Simple config file username/password
    * Local LDAP 
* Automated restore tests
* Restore database through the web UI
* Allow different compression algorithms to be used, or compression to be disabled
* Pruning rules to thin or remove backups over a certain age