# Setting up VoiceHire on another computer

This guide sets up the same app, with the same data, on a second Windows PC.

## What you need to bring

| Item | How to bring it | Why it isn't in GitHub |
|---|---|---|
| The code | `git clone` (step 2) | Already on GitHub |
| `db-backup\VoiceHire.bak` (about 5 MB) | USB drive, OneDrive or email it to yourself | It holds user accounts and password hashes. The repository is public. |
| Your OpenAI API key | Copy it from platform.openai.com when you need it | Secrets are stored per computer, outside the project |

The work PC was set up with: .NET SDK 10.0.303, dotnet-ef 10.0.5, Visual Studio 2026 Community (v18), and SQL Server LocalDB 2019 and 2025.

## 1. Install the tools (one time)

1. **Visual Studio 2026 Community** with the **ASP.NET and web development** workload. This also installs the **.NET 10 SDK** and **SQL Server LocalDB**.
2. **Git for Windows** from https://git-scm.com.
3. The EF Core command-line tool, used when you change the database model:
   ```bash
   dotnet tool install --global dotnet-ef
   ```

To check everything is installed:
```bash
dotnet --list-sdks
```
```bash
sqllocaldb info
```
The first should list a `10.0.x` SDK. The second should list `MSSQLLocalDB`.

## 2. Get the code

```bash
git clone https://github.com/ham-h/interviewproj.git
```

Open `VoiceHire.slnx` in Visual Studio. Set **VoiceHire.Web** as the startup project and choose the **https** profile.

## 3. Restore the database

Copy `VoiceHire.bak` into the cloned folder's `db-backup` folder; create the folder if needed. Git ignores it there. Then open PowerShell in the cloned folder and run:

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\restore-db.ps1 -BackupFile .\db-backup\VoiceHire.bak
```

The database is restored into LocalDB as `VoiceHire`, with its files saved in your user folder. Add `-Force` to replace a `VoiceHire` database that already exists there.

**No backup with you?** Just run the app. In Development it creates an empty `VoiceHire` database with the starter question bank on first start. You'll then need to register a new account.

## 4. Add your OpenAI key

```bash
dotnet user-secrets --project src/VoiceHire.Web set "OpenAI:ApiKey" "<your key>"
```

The app runs without the key, but voice interviews, scoring and importing questions from documents need it.

## 5. Run

Press F5 in Visual Studio, or run:
```bash
dotnet run --project src/VoiceHire.Web --launch-profile https
```

- Sign in with the same account you used at work, since the restored database has it.
- To manage question banks, your email must be listed in `Pilot:AdminEmails` in `src/VoiceHire.Web/appsettings.Development.json`.
- A microphone is needed for interviews. On Windows, check Settings › Privacy & security › Microphone.

## Moving between work and home

Before you leave one computer:
```bash
git add -A
git commit -m "what changed"
git push
```

When you start on the other:
```bash
git pull
```

To carry database changes as well, make a new backup and restore it on the other PC:
```bash
sqlcmd -S "(localdb)\mssqllocaldb" -E -Q "BACKUP DATABASE [VoiceHire] TO DISK = N'%CD%\db-backup\VoiceHire.bak' WITH INIT, COPY_ONLY"
```
Run this from the project folder in Command Prompt. `sqlcmd` comes with SQL Server tools.
