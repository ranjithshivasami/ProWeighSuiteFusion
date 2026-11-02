# ProWeigh Fusion

A single-project WPF desktop application for **ProWeigh Fusion** — weighing
management with live weighing, batches/lots, inward stock, traceability, bay
and tub masters, supplier master, reports and settings.

## Repository layout

```text
proweigh-fusion/
├── ProWeighSuitePlatforms.csproj     # the one and only project (WinExe)
├── App.xaml / App.xaml.cs            # startup, services, crash handling
├── MainWindow.xaml / MainWindow.xaml.cs
├── Program.cs                        # entry point (Velopack + App)
├── Views/                            # every view (XAML + code-behind)
├── ViewModels/                       # every view model + the shell
├── Models/                           # entities and Fusion-specific models
├── Services/                         # business services, sync, print, email...
├── Data/                             # repositories, DB init, connections
├── Hardware/                         # scale, PLC, scanners
├── Converters/                       # XAML value converters
├── Behaviors/                        # attached behaviors (touch, numeric...)
├── Helpers/                          # small static utilities
├── Configuration/                    # app configuration + branding info
├── Infrastructure/                   # licensing, branding, resource monitor
├── Navigation/                       # static nav requests + Fusion routes
├── Security/                         # ICurrentUserContext
├── Licensing/                        # license + branding contracts
├── Capture/  Evaluation/  Workflows/ # weighing capture/evaluation/workflow
├── Contracts/                        # ITransactionRetryQueue
├── Resources/                        # shared XAML styles (colors, menu...)
├── Images/                           # app images
├── branding/fusion/                  # brand profile + images
├── installer/                        # Inno Setup script + build scripts
└── tests/ProWeighFusion.Tests/       # unit tests
```

There is no module system, no host composition and no route/menu registry:
the shell (`ViewModels/ShellViewModel.cs`) hard-codes its menu and creates
view models directly (`new FusionDashboardViewModel()`, ...). Services are
plain classes wired up in `App.xaml.cs`.

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

`branding/fusion/branding.json` is the source of product identity (name,
colors, logo, banner, icon, installer AppId, update feed). It is embedded at
build time and copied to `Branding\branding.json` for optional white-label
overrides. Version lives in `<Version>` in `ProWeighSuitePlatforms.csproj`.

## History

This repository was extracted from
[`ProWeighSuitePlatforms`](https://github.com/ranjithshivasami/ProWeighSuitePlatforms)
at monorepo commit `a19e9d5`, and later consolidated into this single-project
layout.
