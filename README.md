<p align="center">
  <img src="assets/logo.png" width="96" alt="YrClean logo">
</p>

<h1 align="center">🧹 YrClean</h1>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8">
  <img src="https://img.shields.io/badge/UI-WPF-0078D6?logo=windows&logoColor=white" alt="WPF">
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?logo=windows&logoColor=white" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License">
</p>

<p align="center">
  <b>YrClean shows you exactly what it's about to clean, moves it to quarantine instead of deleting it, and never touches executables or system binaries.</b><br>
  Run it by hand or let it run silently on a schedule.
</p>


---

## 🖼️ Interface

<p align="center">
  <img width="978" height="742" alt="image" src="https://github.com/user-attachments/assets/60dcdf0a-6359-4eaf-a18a-a59276f6a8d0" />
</p>

The main window is a single tree: sources → cache groups → individual files, each with its own checkbox, size, and file count. Select what you want, hit **Clean**, confirm the total, done. Right-click anything to open its folder, look it up online, or exclude it from future scans.

The toolbar opens the other modules: **Quarantine**, **System**, **Startup**, **Disk**, **Leftovers**, and settings.

---

## 📉 Example run

> Illustrative example, not a benchmarked claim — your own numbers will depend on what's actually built up on your machine.

```
Before: 1.2 GB across Chrome, Steam, Discord, and NVIDIA shader caches
After:  340 MB left — 860 MB moved to quarantine, 0 files skipped for being protected or in use
```

To measure it on your own machine: scan, note the total, clean, and scan again. There is no separate benchmarking script — it's the same scan the app already shows you. Disk space is actually freed when the quarantine period runs out (7 days by default).

---

## 💡 Why I built this

I wanted a cleaner I trust on my own machine and can recommend to friends without adding "but be careful with it".

Most cleaners are safe only if you configure them that way. YrClean's checks are not settings you can turn off: they run on every file, whether you clicked **Clean** or a scheduled task fired at 3 a.m., and whether the rule came with the app or was downloaded. And because every cleanup goes to quarantine first, a mistake costs you a restore click rather than a reinstall.

The longer-term goal is a tool that makes reinstalling Windows unnecessary: clean up, repair, and see what's slowing the machine down, all in one place.

---

## ⚡ Features

**Cleanup**

- **Interactive scan & review** — results grouped by source, sorted by size, full tree selectable, folders excludable.
- **Quarantine instead of deletion** — cleaned files are moved to a quarantine folder and deleted for good only after a retention period (7 days by default). The **Quarantine** window restores a whole operation or single files.
- **Rule levels** — every rule is `safe`, `review` or `expert`. Only `safe` rules are pre-selected and only `safe` rules run on a schedule; `expert` rules (Prefetch, for example) are never bulk-selected and get an extra warning.
- **Rule engine** — cleanup sources are JSON rules, not code. Built-in rules cover system temp folders, Windows Error Reporting, crash dumps and minidumps, thumbnail cache, Delivery Optimization, GPU shader caches, Discord, Steam, Firefox, all Chromium browsers (Chrome, Edge, Brave, Opera, Vivaldi, Yandex — every profile), and developer caches (Gradle, Maven, npm, pip, NuGet, Docker Desktop logs).
- **Busy detection** — if a rule's program is running, its cache is skipped and marked *busy*.
- **Your own rules** — drop `.json` rule files into the rules folder; optional [winapp2.ini](https://github.com/MoscaDotTo/Winapp2) import (file entries only, always `review` level).
- **Bounded auto-discovery** — finds cache-like folders the rules don't know about. These are `review` level: shown, never selected for you.

**System**

- **Windows cleanup** — DISM component store cleanup, Windows Update download cache, Recycle Bin, and `Windows.old` removal (expert, manual only).
- **Check system** — runs `DISM /RestoreHealth` and then `sfc /scannow` with live output, progress and a plain result.
- **Restore point** — created before DISM and `Windows.old` operations.
- **Health summary** — free space per drive, SMART-based disk health with a warning, number of startup programs.

**Startup, disk, leftovers**

- **Startup manager** — registry Run keys, Startup folders and logon/boot scheduled tasks. Entries are disabled and re-enabled, never deleted. Flags *executable not found* and *unknown publisher*.
- **Disk space** — low free space warning (below 15% by default), largest folders and files, files untouched for 90+ days, duplicates. View only.
- **Program leftovers** — folders in AppData/ProgramData that no installed program seems to own, plus services, scheduled tasks and shortcuts whose program is gone. Review only; folders can be moved to quarantine by hand.

**Automation**

- **Scheduled runs** — hourly/daily/weekly cache cleanup, and a separate optional monthly task for DISM component cleanup. Both launch invisibly.
- **Command line** — `--auto-clean`, `--health`, `--startup-report`, `--disk-report`.
- **Optional notifications** — a Windows balloon notification after each scheduled run (off by default); a low disk space warning is always shown.
- **Logs** — a plain-text log and a JSON journal of every operation.

**Languages**

English, Ukrainian, Russian and Polish. YrClean follows your Windows display language and falls back to English; you can pick a language in settings. Log files and command-line reports are always in English.

### Safety model

<details>
<summary><b>🛡️ Details (click to expand)</b></summary>
<br>

Every file goes through the same checks whether you click **Clean** or a scheduled task fires unattended, and whether the rule is built-in, written by you, imported from winapp2.ini or downloaded:

| Check | What it prevents |
|---|---|
| Allowed-root check | The file must sit inside one of the scanned roots — nothing outside that boundary can ever be touched |
| Protected locations | `Windows`, `Program Files`, drive roots, your profile root, Documents, Desktop, Pictures, Music, Videos and Downloads are off limits to every rule. The only exceptions are a fixed list of cache folders inside `Windows` (`Temp`, `Prefetch`, `Minidump`, `LiveKernelReports`, `SoftwareDistribution\Download`, Delivery Optimization cache) that is compiled into the app and cannot be extended by a rule |
| Protected extensions | `.exe`, `.dll`, `.sys`, `.msi`, `.msp`, `.ocx`, `.drv`, `.com`, `.scr`, `.cpl`, `.efi`, `.mui`, `.lnk`, `.ini`, `.reg`, `.bat`, `.cmd`, `.ps1`, `.vbs`, `.js`, `.jse`, `.wsf`, `.hta` are never removed, no matter where found |
| Reparse points | A file is skipped if it, or any folder between it and its root, is a symlink or junction |
| Minimum file age | Files read or modified more recently than the threshold (14 days by default) are left alone. A rule can make this stricter, never looser |
| Exclusions | Any folder you exclude takes priority over every rule |
| Quarantine | Files that pass every check are moved, not deleted, and can be restored until the retention period ends |

Rules are also validated when they are loaded: a rule with a relative path, an unknown variable, or a path that reaches a protected location is rejected outright. Paths are compared in canonical form, so 8.3 short names (`C:\PROGRA~1`) are recognised for what they are.

Restoring from quarantine is checked too: a file is only put back to a location that would have been allowed for cleaning, and never over an existing file.

**What does not go through quarantine:** emptying the Recycle Bin, DISM operations and removing `Windows.old` are done by Windows itself and cannot be undone. Each asks for confirmation first.

**What YrClean does not do:** it does not clean the registry, tweak services, or disable telemetry.

**What gets scanned:**

| Category | Examples |
|---|---|
| System | User & system `Temp`, Windows Error Reporting, crash dumps, minidumps, thumbnail cache; Delivery Optimization (review); `Prefetch` (expert) |
| GPU shader caches | NVIDIA `GLCache` / `DXCache`, AMD `DxCache` / `GLCache` / `VkCache` |
| Browsers | Chrome, Edge, Brave, Opera, Vivaldi, Yandex Browser (all profiles), Firefox |
| Messengers | Discord `Cache` and `Code Cache` |
| Games | Steam's embedded-browser (`htmlcache`) cache |
| Developer (review) | Gradle caches, Maven repository, npm cache, pip cache, NuGet HTTP cache, Docker Desktop logs |
| Auto-discovered (review) | Any `*cache*`, `tmp`, `temp`, or `logs`-style folder under `%LocalAppData%`, `%AppData%`, and `%ProgramData%` |

Auto-discovery walks up to **4 folders deep** and stops after **20,000 folders visited**. Anything with `config`, `settings`, `save`, `profile`, `credentials`, `keys`, `wallet`, `package cache`, `installer` or similar in its path is skipped automatically, even if the folder name also looks like a cache.

</details>

---

## 📦 Install

No installer yet — grab the source, build it, and run the `.exe`:

```powershell
git clone https://github.com/nikita-yr/yr-clean.git
cd yr-clean
dotnet build YrClean.sln -c Release
YrClean.UI\bin\Release\net8.0-windows\YrClean.UI.exe
```

## 🔧 Build

Debug build:

```powershell
dotnet build YrClean.sln
```

Release build:

```powershell
dotnet build YrClean.sln -c Release
```

Executable lands at `YrClean.UI\bin\<Debug|Release>\net8.0-windows\YrClean.UI.exe`.

Tests:

```powershell
dotnet test YrClean.sln
```

### Publish a standalone build

```powershell
dotnet publish YrClean.UI -p:PublishProfile=win-x64
```

This produces a single self-contained `YrClean.UI.exe` (no .NET install needed on the target machine) in the `publish` folder, next to `invisible.vbs`. Keep the two files together: the scheduled tasks use `invisible.vbs` to start YrClean without a window.

### SmartScreen warning

The `.exe` is not code-signed, so the first time you run a downloaded or published copy, Windows SmartScreen may show *"Windows protected your PC"*. Click **More info → Run anyway**. If you'd rather not trust a binary, build it from source as shown above — a locally built file does not trigger the warning.

## ▶️ Run

```powershell
YrClean.UI\bin\Release\net8.0-windows\YrClean.UI.exe
```

YrClean asks for administrator rights when it starts (one UAC prompt). Open settings (the gear icon) to configure the schedule, minimum file age, quarantine retention, low disk space threshold, language, and rule updates.

Command-line switches:

| Switch | What it does |
|---|---|
| `--auto-clean` | Unattended cleanup of the enabled `safe` rules — this is what the scheduled task runs |
| `--health` | `DISM /RestoreHealth`, then `sfc /scannow` |
| `--startup-report` | Lists everything that starts with Windows |
| `--disk-report` | Free space, disk health, largest folders and files in your profile |
| `--component-cleanup` | DISM component store cleanup — this is what the monthly task runs |

Reports are printed to the console when YrClean is started from an elevated one, and are always saved to `%AppData%\YrClean\reports`.

### Rules

A rule file looks like this:

```json
{
  "schema": 1,
  "rules": [
    {
      "id": "my-app-cache",
      "name": "My App cache",
      "category": "Apps",
      "level": "review",
      "detect": ["app:My App"],
      "process": ["myapp"],
      "minAgeDays": 30,
      "paths": [
        "%LocalAppData%\\MyApp\\Cache",
        { "path": "%LocalAppData%\\MyApp\\Logs", "masks": ["*.log"], "recurse": false }
      ]
    }
  ]
}
```

- `level` — `safe`, `review` or `expert`.
- `detect` — the rule is shown only if one entry matches: an existing path, a registry key (`HKCU\...`), or an installed program (`app:Name`). Leave it out to always show the rule when its folders exist.
- `process` — while one of these programs is running, the rule is skipped as *busy*.
- `paths` — folders to clean; `*` matches any single folder name (this is how browser profiles are enumerated).

Put your own rule files in `%AppData%\YrClean\rules`. In settings, **Update rules from GitHub** downloads the latest built-in rule base from this repository (HTTPS only, validated before it is saved), and **Download winapp2.ini** fetches the community rule file from its own repository. Neither happens automatically — YrClean only goes online when you press one of those buttons.

## 🖥️ Requirements

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (to build)
- WPF and Windows Forms desktop workloads (installed alongside the .NET 8 SDK)

## 📁 Where things live

| Path | Contents |
|---|---|
| `%AppData%\YrClean\settings.json` | Your saved settings |
| `%AppData%\YrClean\autoclean.log` | Plain-text log: one line per run, plus errors |
| `%AppData%\YrClean\journal.jsonl` | JSON journal of every operation (one object per line) |
| `%AppData%\YrClean\rules` | Your own rule files, the downloaded rule base (`remote.json`) and `winapp2.ini` |
| `%AppData%\YrClean\reports` | Reports produced by the command-line switches |
| `%LocalAppData%\YrClean\Quarantine` | Cleaned files waiting out their retention period, one folder per operation |

---

## ❓ FAQ

<details>
<summary><b>Can YrClean delete something important by mistake?</b></summary>
<br>

Every file is checked against the allowed roots, protected locations, protected extensions, reparse-point rules, the minimum-age threshold and your exclusions — see [Safety model](#safety-model). And nothing is deleted on the spot: files go to quarantine, where you can restore them for 7 days (configurable).
</details>

<details>
<summary><b>Does it need administrator rights?</b></summary>
<br>

Yes. YrClean asks for them once, when it starts. It needs them for system temp folders, DISM, sfc, the Windows Update cache and system-wide startup entries. It never elevates silently in the background.
</details>

<details>
<summary><b>Why didn't my disk space go up right after cleaning?</b></summary>
<br>

Cleaned files sit in quarantine until the retention period ends, and only then are deleted. If you need the space immediately, lower the retention period in settings.
</details>

<details>
<summary><b>What do the scheduled tasks actually run?</b></summary>
<br>

`schtasks.exe` registers "YrClean AutoClean", which launches `YrClean.UI.exe --auto-clean` through a tiny invisible `.vbs` wrapper, so no console or window appears. It cleans only the `safe` rules, applies your saved settings and exits. If you enable monthly maintenance, a second task, "YrClean Maintenance", runs `--component-cleanup` the same way.
</details>

<details>
<summary><b>Can I exclude specific folders?</b></summary>
<br>

Yes — right-click a source or a folder in the tree and choose **Add to exclusions**. Exclusions always beat a rule.
</details>

<details>
<summary><b>Is winapp2.ini included?</b></summary>
<br>

No. It is licensed CC BY-SA 4.0 by its authors, so YrClean does not ship it; the settings window can download it for you from its own repository. Only its file entries are used — registry entries are ignored — and every imported rule is `review` level, so it is never run on a schedule.
</details>

---

## 🗺️ Roadmap

- [x] Dark theme
- [x] Quarantine with restore
- [x] JSON-formatted log (alongside the plain-text one)
- [x] JSON rule engine, winapp2.ini import
- [x] Windows cleanup and repair tools (DISM, sfc, update cache, restore points)
- [x] Startup manager, disk analyzer, program leftovers
- [x] Localization (English, Ukrainian, Russian, Polish)
- [x] Portable / single-file build
- [ ] UI redesign pass
- [ ] Signed release binaries
- [ ] Verify startup publishers by digital signature instead of file metadata
- [ ] Other Disk Cleanup handlers beyond `Windows.old`
- [ ] Docker image and build cache pruning

Have an idea or a bug? Open an issue — see below.

## 🤝 Contributing

Issues and PRs are welcome. Before sending a PR:

1. Keep changes focused — one feature or fix per PR.
2. If you're touching `SafeDeleteService`, `ProtectedLocations`, `QuarantineService` or anything else in the delete path, explain the reasoning in the PR description; that code is deliberately conservative.
3. `dotnet build YrClean.sln` should succeed with no warnings and `dotnet test YrClean.sln` should pass before you push.

## 📄 License

Licensed under the [MIT License](LICENSE).
