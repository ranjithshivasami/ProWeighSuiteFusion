# ProWeigh Fusion

A focused, single-product repository for **ProWeigh Fusion** — extracted from the
`ProWeighSuitePlatforms` monorepo. It contains the shared core platform (login,
shell, Master, Configuration, Audit logs, Users, Permissions, Info, Logout,
Exit) and the Fusion product module, consolidated into three projects.

## Repository layout

```text
proweigh-fusion/
├── src/
│   ├── ProWeighSuitePlatforms.Core/          # consolidated core library
│   ├── ProWeighSuitePlatforms.Modules.Fusion/ # Fusion product module
│   └── ProWeighSuitePlatforms.Fusion/         # Fusion desktop app (WinExe)
├── branding/
│   └── fusion/                                # brand profile, images, version
├── installer/                                 # Inno Setup script + build scripts
├── tests/
│   └── ProWeighFusion.Tests/                  # unit + architecture tests
├── Branding.targets                           # brand embedding/validation
├── Directory.Build.props / Directory.Packages.props
└── global.json
```

### Consolidation

The monorepo's nine shared projects were merged into a single
`ProWeighSuitePlatforms.Core` library:

| Former project | Now |
|---|---|
| Core.Abstractions, Core, Core.Wpf | `ProWeighSuitePlatforms.Core` |
| Platform.Hardware, Platform.Reporting, Platform.Reporting.Wpf | `ProWeighSuitePlatforms.Core` |
| Platform.Weighing, Platform.Weighing.Wpf, Host.Wpf | `ProWeighSuitePlatforms.Core` |

Namespaces were left unchanged, so the code works exactly as before with far
less project ceremony. The BOM meta-package, the DevHarness host, and the other
product modules (Nexus, BatchWeighing, Hybrid, Standard, Inventory) are not
part of this repository.

### Core platform functionality

- **Login** and user session (`LoginView` / `LoginViewModel`)
- **Master** — Category, Variant, Materials, Supplier
- **Configuration** — Scale, Company, App, Sync, Backup, Report schedule,
  Label templates, Printer, PLC, Scanner, Cleanup, Failed syncs
- **Audit logs** — viewer and audit service with offline sync
- **Users** and **Permissions** — user management and the role/permissions matrix
- **Info** — about screen
- **Logout / Exit** — shell-level actions
- Shared shell, theme engine, navigation, weighing, reporting, hardware (serial,
  PLC, scanners) and hosting infrastructure.

### Fusion product module

- Fusion Dashboard, Live View, Batch/Lots, Inward Stock, Traceability
- Bay Status, Equipment Health, Tub Master, Supplier Master, Support Tickets
- Fusion Settings and Reports
- Fusion data access (`Data/`) and PLC run service (`Services/`)

## Build

```powershell
dotnet build ProWeighFusion.sln -c Release
dotnet test tests/ProWeighFusion.Tests -c Release
```

## Publish / installer

```powershell
installer\publish-release.bat      # single-file self-contained publish
installer\build-installer.bat      # publish + Inno Setup installer
```

## Branding

`branding/fusion/branding.json` is the single source of product identity
(name, colors, logo, banner, icon, installer AppId, update feed). It is
embedded at build time and validated by `Branding.targets`
(`ValidateHostBranding`). Version lives in `branding/fusion/Version.props`.

## History

This repository was extracted from
[`ProWeighSuitePlatforms`](https://github.com/ranjithshivasami/ProWeighSuitePlatforms)
at monorepo commit `a19e9d5`. The monorepo remains the source of history and
continues to host the other products.
