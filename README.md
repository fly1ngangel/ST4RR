<p align="center">
  <img src="src/logo.png" alt="ST4RR star" width="128" height="128">
</p>

<h1 align="center">ST4RR</h1>

<p align="center">A Brawl Stars player finder for Windows.</p>

ST4RR helps you find players and keep a local copy of their profiles and available battles. You can search without an API key, compare accounts, and come back to anything you've saved.

## Features

- Search by name or player tag.
- Filter saved players by trophies, club, brawlers, account year, and battle dates.
- Compare profiles, save favorites, and add notes.
- Browse saved battles, trophy history, club members, and rankings.
- Import, export, and back up your data.
- Switch between English and Russian. The first launch uses your Windows language.

## Getting started

Run the installer, or extract the portable archive and open `ST4RR.exe`. Enter a name or player tag and click **Search online**.

ST4RR needs .NET Framework 4.8. The installer includes it; the portable version needs it installed already.

The app targets Windows 8.1 Update, 10, and 11. It has been tested on Windows 11. Windows 8.1 still needs testing on a real system.

## Data sources

| Source | Used for |
| --- | --- |
| BrawlFind | Name and tag searches, profiles, trophy history, clubs, and rankings |
| Brawl Time Ninja | Profiles, brawlers, available battles, and events |
| Supercell API | Direct requests with your own API key, if you add one |
| Brawlify | Opening a player's profile in your browser |

Each record shows where it came from. You can choose which sources to use in Settings. Public sites may have outdated data or change how their pages work.

## A few limits

ST4RR can't tell you an account's exact creation date or last login. The year filter uses information you've added or imported. Battle date filters only search your saved battles, and the most recent battle isn't the same as the last login.

It can't recover battles that are no longer available from a source unless you've already saved them.

## Building

You'll need Windows, .NET Framework 4.8, and Python 3. From the project folder, run:

```powershell
python tools/bootstrap.py
powershell -ExecutionPolicy Bypass -File build.ps1
```

The app will be in `build/ST4RR.exe`. Keep the DLLs and config file beside it. The bootstrap script downloads the compiler and dependencies from NuGet, so you don't need Visual Studio.

To build the installer, use Inno Setup 6.7.3. Save Microsoft's [.NET Framework 4.8 offline installer](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48) as `tools/net48.exe`, then compile `installer.iss`. The result goes in `dist/`.

## Tests

```powershell
powershell -ExecutionPolicy Bypass -File test.ps1
powershell -ExecutionPolicy Bypass -File test.ps1 -UI
powershell -ExecutionPolicy Bypass -File test.ps1 -Network
```

The default tests use small sample files. `-UI` checks both languages on a separate desktop. `-Network` checks the live services and needs an internet connection.

## Local data

Profiles and settings are stored in `%LOCALAPPDATA%\ST4RRF1ND`. The old folder name is kept for compatibility. Uninstalling the app leaves your data there.

If you add an API key, it's encrypted for your Windows user and sent only to `api.brawlstars.com`. It isn't included in backups.

## License

[MIT](LICENSE), copyright 2026 fly1ngangel. Library licenses are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

ST4RR is unofficial and isn't affiliated with Supercell or the listed data providers. Brawl Stars and related materials belong to Supercell. Third-party data and materials aren't covered by this project's MIT license.

This material is unofficial and is not endorsed by Supercell. For more information, see [Supercell's Fan Content Policy](https://supercell.com/en/fan-content-policy/).
