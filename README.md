# MySql Backup Agent

This is intended to be a user friendly, easily deployable, containerized service that can perform backups of multiple MySQL/MariaDB databases on individual cron-style schedules.  Has a web interface built on ASP.NET Core 3.1 using server-side Blazor.

Only works for MySQL compatible databases, as it uses the `MySqlBackup.Net` project, which in turn works with `MySql.Data`.

Currently this project is in an early proof of concept stage, and has the following capabilites:

* Cron-style scheduling of backups
* Multiple database targets, each with their own schedule
* Can check the last time the database was updated and skip the backup if it isn't necessary (needs MySQL 5.7.2 or later)
* Compresses the backup with gzip 
* Uploads the backup to an S3 compatible storage (Minio is what I use for self-hosted on prem backup)

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
* Logging to allow observability of backup and restore failures
* Alternate storage backends for network shares or local storage
* Webhooks for success/failure on operations

## Configuration

All application configuration is done through the standard ASP.NET Core `appsettings.json` file.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  
  "Storage": {
    "Type": "S3",
    "Endpoint": "minio.example.com:9000",
    "AccessKey": "super-secure-access-key",
    "SecretKey": "super-secure-secret-key",
    "Location": "",
    "Bucket": "my-bucket-name",
    "Prefix": "my-prefix-name"
  },
  
  "BackupTargets": {
    "Demo": {
      "Name": "Employees Test Database",
      "ConnectionString": "server=db0.example.com;database=employees;uid=backup_user;pwd=testpass",
      "CheckForUpdate": true,
      "Cron": "*/30 * * * *"
    },

    "Demo2": {
      "Name": "Employees Test Database 2",
      "ConnectionString": "server=db1.example.com;database=employees;uid=backup_user;pwd=testpass",
      "CheckForUpdate": false,
      "Cron": "*/60 * * * *"
    }

  }
}

```

## Design Decisions

The following are design decisions made about the software.

### MySQL Only

Originally I looked into making this work with Postgres as well, however, as far as a backup solution went there weren't any options aside from `pgdump`, which would then constrain the software such that a binary would have to be installed in the Asp.NET Core environment.  For a docker image this seemed reasonable, but I wasn't sure at this point if this is a constraint I wanted to adopt.

Extending the software to work with other databases will require some slight surgery on the class taxonomy, specifically breaking a few classes out to interfaces and/or wrapping an underlying backup/restore provider interface that can encapsulate the differences.  Since my use case was primarily MySQL, I stuck with the simple version for now.

### Database Timestamps

I chose to use the database's reported time as the timestamp saved with each backup, but use the runtime environment's time for the scheduling.

In a sane environment, these will be very, very close to each other.  However, in the case that they're not, my rationale for splitting the times up is as follows:

* In MySQL, the last updated time of the tables is found through a query of `information_schema.tables`, and the time it returns is based on the database server's own internal clock. 
* If we save database backups with timestamps from the ASP.NET environment's clock and this differs significantly from the MySQL server clock, it's possible that we can get in a situation where the database has been updated at a time after the last backup was taken, but before the ASP.NET environment's clock said the backup was taken.  In this case a backup target set to check for updates would not take a backup even though there was new data in the database.
* There is no easy way to schedule backups in the ASP.NET environment based on the MySQL database's clock. We can measure and maintain a clock offset between the two systems, but handling changes as clocks are adjusted runs into common time synchronization issues.
* Scheduling based on the ASP.NET environment and then storing a per-backup time offset to get around the possibility of clocks being adjusted would have required more than the simple filename method of storage I wanted to preserve to make the service easy to deploy.
* In this case it felt like using the cron style scheduling in the ASP.NET environment would meet the intention of a backup system while using the database clock for timestamps would avoid any issues with the system not knowing whether a database had been updated since the last backup.