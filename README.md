# TIA Git Add-In

TIA Portal V21 Add-In for working with Git from inside TIA Portal Version Control (VCI) workflows (finally).

The add-in targets `.NET Framework 4.8` and packages as a native TIA Portal V21 `.addin` file. It provides a specialized Git panel integrated directly into the TIA Portal UI, focusing on the unique needs of PLC developers using VCI.

![overview](docs/overview.png)

## Features & Status

### Native SimaticML Decoding

- **Zero Dependencies**: Completely removed dependencies on Node.js and external Siemens ACTool scripting.
- **Native Parser**: Custom C# `XDocument`-based parser for SimaticML files.
- **Structural Comparison**: Logic and interface are compared directly in C#, allowing for deep semantic diffing of PLC blocks.

### Visual LAD Diff

- **Side-by-Side View**: Compare "OLD" (parent) and "NEW" (commit/working tree) LAD networks horizontally.
- **Single-Commit State Detection**: Commit diff mode compares the selected commit against its parent, so the view reflects the state introduced by that commit.
- **Graphic Rendering**: Simplified geometric representation of Ladder Logic, including contacts, coils, boxes, powerrails, and orthogonal wires.
- **Branching Layout**: Custom layout engine handles multiple rungs and branch routing while hiding SimaticML-only routing helper nodes from the visual view.
- **Element-Level Highlighting**: Only the affected LAD components are colored, not the whole network. Yellow means changed or rewired, green means added on the new side, and red means removed from the old side.
- **Topology-Aware Changes**: Components are marked changed when their wiring moves, even if the underlying SimaticML part/call definition stays the same.

### PLC Interface Comparison

Parsed PLC blocks include a structured interface comparison beside the LAD view. It reports changes in name, datatype, retain, start and default values, comments, accessibility, informative flag, version, and the member attributes `ExternalAccessible`, `ExternalVisible`, `ExternalWritable`, and `SetPoint`.

### HMI and hardware in Git

TIA Portal VCI cannot contain an HMI device. The Git panel has **HMI** and **Hardware** checkboxes for export and the same pair for import, with one **Export** button and one **Import** button. Export writes `HmiExport/<device>/` and `HardwareExport/`. Import applies the checked folders back onto the matching devices in the open project. A checkout only puts the files on disk; **Import** is a separate step.

While an export runs, a checklist stays open. It is grouped by device and shows each category, the item being written, and the count when that category finishes. Export keeps a creation time that is already stored in a file, and leaves a new one out when the file had none, so an unchanged device does not rewrite every file.

**Yes** means that item is written and read back. **Partly** means the file or the write-back is limited; the limit is in the Notes column. **No** means that item is left out. A checkout only puts files on disk. **Import** is a separate step.

| Area | Item | Export | Import | Notes |
| --- | --- | --- | --- | --- |
| Unified | Screens and every widget Openness exposes | Partly | Partly | Screen properties, events, dynamizations, and appearance. A widget is created only when Openness has that type. Widgets missing from the file are removed |
| Unified | Tag tables and tag groups | Yes | Partly | Tag tables missing from the folder stay in the project |
| Unified | System tags | Partly | Partly | Writable attributes on the matching tag. System tags are not created or deleted |
| Unified | Plant-object tags | Partly | Partly | Writable attributes on the matching tag. Plant-object tags are not created or deleted |
| Unified | JavaScript modules | Yes | Yes | |
| Unified | Text lists | Yes | Yes | |
| Unified | System text lists | Yes | Yes | |
| Unified | Graphic lists | Yes | Yes | |
| Unified | Discrete alarms, analog alarms, alarm classes | Partly | Partly | Attributes and texts. A new alarm is created when the composition allows it. Alarms missing from the file are removed |
| Unified | OPC UA alarm types and alarm audit classes | Partly | Partly | Attributes. Objects missing from the file are removed when the composition can delete them |
| Unified | Connections, data logs, alarm logs, audit trails | Partly | Partly | Attributes. Objects missing from the file are removed when the composition can delete them |
| Unified | Runtime settings | Partly | Partly | Attributes Openness exposes |
| Unified | Any other composition Openness lists | Partly | Partly | Stored under `Other/` |
| Unified | Recipes and parameter sets | No | No | V21 Openness has no recipe or parameter-set object |
| Unified | Users, cycles, and device settings | No | No | V21 Openness has no object for these |
| Unified | Images stored only on the device | No | No | Images in the project graphic collection are covered under Project |
| Comfort | Screens and screen folders | Yes | Yes | Screens missing from the folder are deleted |
| Comfort | Popups, templates, slide-in screens | Yes | Yes | Objects missing from the folder are deleted |
| Comfort | Tag tables | Yes | Partly | User tables missing from the folder are deleted. System tables stay |
| Comfort | VB scripts | Yes | Yes | Scripts missing from the folder are deleted |
| Comfort | Text lists | Yes | Yes | Lists missing from the folder are deleted |
| Comfort | Connections | Yes | Yes | Connections missing from the folder are deleted |
| Comfort | User cycles | Yes | Yes | User cycles missing from the folder are deleted |
| Comfort | System cycles | Yes | Partly | Applied when Openness accepts the file. System cycles are not created or deleted |
| Comfort | Permanent area and overview | Yes | Yes | |
| Comfort | Graphic lists | Partly | Partly | Attribute snapshot. Siemens `GraphicList.Export` is not called |
| Comfort | Alarms and messages | Partly | Partly | Compositions whose name contains "alarm" or "message" |
| Comfort | Other compositions Openness lists | Partly | Partly | Attribute snapshot, plus a Siemens graphic file when the composition exports one |
| Comfort | Recipes | Partly | Partly | Only when Openness lists a recipe composition. The V21 public API has no recipe type |
| Comfort | Users, groups, scheduled jobs, runtime settings, device settings, fonts | No | No | Fonts set on a screen object travel with that screen |
| Project | Project pictures | Yes | Yes | Image files are included. Pictures missing from the folder are removed |
| Project | Faceplate types in the project library | Yes | Partly | The in-work version is used when one exists. A missing type is created. Types missing from the folder stay. Global libraries are left out |
| Project | Styles and style sheets in the project library | Yes | Partly | Same limits as faceplate types |
| Hardware | Device and module tree, type, rack position, PLC I/O addresses | Yes | Partly | Attributes on the matching device and modules. Devices and modules are not created, deleted, or moved |
| Hardware | Subnet name, type, and IO system | Yes | Yes | |
| Hardware | Node IP address, subnet mask, router, PROFINET name | Yes | Yes | Written when Openness provides the value |
| Hardware | Port-to-port links | Yes | Yes | |
| Hardware | SINAMICS parameters that differ from the factory default | Yes | Yes | |
| Hardware | SINAMICS security settings | Yes | Yes | |
| Hardware | SINAMICS telegram type | Yes | Partly | Written when Openness accepts the change |
| Hardware | Drive function settings | Yes | Yes | Function settings and technology extensions on the matching module |
| PLC | PLC blocks | Yes | No | TIA Portal VCI writes the files. There is no PLC import button. **Restore** checks the commit out on a `restore-<short hash>` branch |

The sections below say how each exported file is written. **Full** means a Siemens export or import. **Snapshot** means the file stores the attributes and child objects Openness exposes.

#### WinCC Unified

| Item | Export | Import |
| --- | --- | --- |
| Screens, including screens in screen groups | Snapshot of the folder, size, screen properties, and every widget Openness exposes, with events, dynamizations, and appearance. Buttons and I/O fields also store text and process value | Screen name, size, properties, events, dynamizations, and appearance. Widgets are created when Openness has that type, and widgets missing from the file are removed |
| Tag tables and tag groups | Full Siemens tag export | Full Siemens tag import. Tag tables that are missing from the folder stay in the project |
| System tags | Snapshot | Writable attributes on the matching tag. System tags are not created or deleted |
| Plant-object tags | Snapshot | Writable attributes on the matching tag. Plant-object tags are not created or deleted |
| JavaScript modules | Full Siemens script export | Full Siemens script import |
| Text lists | Full | Full |
| System text lists | Full | Full |
| Graphic lists | Full | Full |
| Discrete alarms, analog alarms, alarm classes | Snapshot, including event text and info text | Attributes and texts on the matching alarm. A new alarm is created when the composition allows it. Alarms missing from the file are removed |
| OPC UA alarm types, alarm audit classes | Snapshot | Attributes on the matching object. Objects missing from the file are removed when the composition can delete them |
| Connections, data logs, alarm logs, audit trails | Snapshot | Attributes on the matching object. Objects missing from the file are removed when the composition can delete them |
| Runtime settings | Snapshot | Attributes on the runtime settings |
| Any other composition Openness lists | Snapshot under `Other/` | Attributes on the matching composition. Objects missing from the file are removed when the composition can delete them |
| Recipes and parameter sets | None. V21 Openness has no recipe or parameter-set object | None |
| Users, cycles, and device settings | None | None |
| Images stored only on the device, outside the project graphic collection | None | None |

A faceplate placed on a screen is part of that screen's widget snapshot. Faceplate types and styles in the project library are exported with the project pictures. Types in a global library are left out.

#### WinCC Comfort

| Item | Export | Import |
| --- | --- | --- |
| Screens and screen folders | Full Siemens screen export | Full. Screens missing from the folder are deleted |
| Popups, templates, slide-in screens | Full | Full. Objects missing from the folder are deleted |
| Tag tables | Full | Full for user tables. User tables missing from the folder are deleted. System tables stay |
| VB scripts | Full | Full. Scripts missing from the folder are deleted |
| Text lists | Full | Full. Lists missing from the folder are deleted |
| Connections | Full | Full. Connections missing from the folder are deleted |
| User cycles | Full | Full. User cycles missing from the folder are deleted |
| System cycles | Full Siemens cycle export | The file is applied when Openness accepts it. System cycles are not created or deleted |
| Permanent area and overview | Full | Full |
| Graphic lists | Snapshot. Siemens `GraphicList.Export` is not called | Attributes and list entries on the matching graphic list |
| Alarms and messages | Snapshot of compositions whose name contains "alarm" or "message" | Attributes on the matching object. Objects missing from the file are removed when the composition can delete them |
| Graphics and any other composition Openness lists | Snapshot, plus a Siemens graphic file when the composition exports one | Siemens graphic files are imported. Attribute snapshots are written onto the matching composition |
| Recipes | Snapshot only when Openness lists a recipe composition. The V21 public API has no recipe type | Attributes on that composition when the snapshot exists |
| Users and groups, scheduled jobs, runtime settings, device settings, and fonts | None. Fonts set on a screen object travel with that screen | None |

#### Project

Pictures in the project graphic collection, and faceplate types, styles, and style sheets in the project library, are written to `HmiExport/_Project/<project>/`. Import applies them before the HMI devices, so a screen can find its faceplate type.

| Item | Export | Import |
| --- | --- | --- |
| Project pictures | Full Siemens graphic export, including the image files | Full. Pictures missing from the folder are removed |
| Faceplate types in the project library | Full library document. The in-work version is used when one exists | A missing type is created. An existing type updates its in-work version. Types missing from the folder stay. Global libraries are left out |
| Styles and style sheets in the project library | Full library document. The in-work version is used when one exists | A missing type is created. An existing type updates its in-work version. Types missing from the folder stay. Global libraries are left out |

#### Hardware

With **Hardware** checked, export writes `HardwareExport/` for every device in the project, including devices that are not in a group. PLC blocks stay on the VCI path.

| Item | Export | Import |
| --- | --- | --- |
| Device and module tree, type, rack position, PLC I/O addresses | Full, in `Hardware.xml` | Attributes on the matching device and modules. Devices and modules are not created, deleted, or moved |
| Subnet name, type, and IO system | Full | Attributes on the matching subnet |
| Node IP address, subnet mask, router, PROFINET device name | Full when Openness provides the value | Full, on the matching node |
| Port-to-port links | Full | Full, on the matching ports |
| SINAMICS parameters whose value differs from the factory default | Full | Full. The value is written onto the matching drive |
| SINAMICS telegrams and security settings | Full | Security settings on the matching drive. The telegram type is written when Openness accepts the change |
| Drive function settings under `Settings/` | Full | Function settings and technology extensions on the matching module |

#### PLC blocks

TIA Portal VCI writes PLC blocks into the Git workspace. The add-in compares those SimaticML files. There is no separate PLC import button. **Restore** checks the commit's files out on a `restore-<short hash>` branch.

### TIA Portal V21 Compare API Decision

TIA Portal V21's supported Public API includes in-process, data-only comparison for TIA engineering objects (`PlcSoftware.CompareTo`, `PlcSoftware.CompareToOnline`, hardware/library comparisons) and coarse VCI mapped-object status. The engineering-object methods return comparison result trees; VCI exposes status data only. Neither surface provides Siemens' graphical LAD/FBD comparison editor or accepts Git/SimaticML revision files. The V21 Add-In API exposes no supported compare/diff UI entry point.

Git revision review therefore continues to use the project-owned native SimaticML comparer and custom LAD renderer, with the text/structured view as fallback. Internal `Siemens.Automation.CommonServices.Compare.*` UI and command types are not used. See the [V21 compare API investigation](docs/tia-v21-compare-api-investigation.md) for the evidence and decision matrix.

### Git Integration

- **VCI-Aware**: Uses the selected TIA Portal VCI workspace as the Git working tree.
- **First use**: If that folder is not a Git repository yet, the add-in asks to create one there. You do not need to open a command prompt.
- **Core Operations**: View status, history, branches, and commit changes without leaving TIA Portal. The History and Branch pages load when you open them. **Show changes** on Status compares the current workspace files with the last commit. Files can be staged when the workspace path contains spaces.
- **Export page**: Select a commit on History. The Export page lists the HMI and hardware files stored in that commit, grouped by device and folder.
- **About page**: Shows the add-in version and the same export and import coverage as the tables above. Shaded rows are left out of export, import, or both.
- **Text diff navigation**: In a text diff, **Previous change** and **Next change** scroll to the previous and next changed block.
- **Restore**: Restoring a commit creates a branch named `restore-<short hash>` and checks it out. The original branch is left unchanged.
- **Commit identity**: In Settings, **Your Name** and **Your Email** are saved into the repository so commits made from TIA Portal have an author. When those boxes are empty, Settings fills them from the name and email already stored for this repository.
- **Transparent Execution**: Uses your local `git.exe`. It is found on `PATH`, or at `C:\Program Files\Git\cmd\git.exe`.
- **Remote repository**: In Settings, set **Default Remote** (usually `origin`) and **Remote URL**, then save. The URL can be a GitHub address (`https://github.com/org/repo.git` or `git@github.com:org/repo.git`) or an Azure DevOps address (`https://dev.azure.com/org/project/_git/repo`, a `visualstudio.com` URL, an Azure DevOps Server URL containing `/_git/`, or `git@ssh.dev.azure.com:v3/org/project/repo`). Fetch, Pull, and Push use that remote name. Leave the password out of the URL. Sign in once with `git fetch` from a command prompt in the workspace folder so Git Credential Manager can store the credentials.
- **PDF export**: History exports the loaded commits and, when a commit is selected, its changed files. Diff exports the changed-file list and the open text or interface comparison. Ladder diagrams are left out of the PDF.

Exported XML, including HMI and hardware files, is compared by element and attribute. A changed value is shown on its own line, such as `Item[@name="Start"]/@text`. Line breaks and the order of attributes are ignored. In Settings, **XML changes** can switch that view back to plain text, which shows the file line by line. SCL and other plain text files still show added lines in green and removed lines in red. The note that semantic comparison is unavailable means that file is being compared as text. LAD blocks that can be parsed use the graphical comparison instead.

## Prerequisites

- **TIA Portal V21** (required for the Add-In API and Publisher).
- **.NET SDK** compatible with the repo `global.json`.
- **Git for Windows**. The add-in looks for `git.exe` on `PATH` and in `C:\Program Files\Git\cmd\git.exe`.

## Build & Installation

### Build (only for locally built Add-In)

```powershell
dotnet restore TiaGitAddIn.sln
dotnet build TiaGitAddIn.sln --no-restore
```

The build process automatically packages the result into a `.addin` file using the Siemens Add-In Publisher:
`src/TiaGitAddIn/bin/Debug/net48/TiaGitAddIn.addin`

### Installation

The repository includes `installer/out/plugin_installer.exe`. That file embeds a Release add-in from when it was last built, so it can be run without compiling. It checks that the destination is the TIA Portal V21 `UserAddIns` folder. If that folder is missing or is not the right one, you can browse to `Portal V21\UserAddIns`. It then copies the package there, tells you which add-in version was installed, and lists the steps to enable it in the Add-ins task card. If Git for Windows is not installed, it offers to open the download page. Close TIA Portal first if the add-in file is already in use.

To install the current source, build a Release package and rebuild the installer. The HMI export added after that exe was published is only in a freshly built package.

```powershell
dotnet build TiaGitAddIn.sln --configuration Release
python installer/build_plugin_installer.py
.\installer\out\plugin_installer.exe
```

Starting `python installer/build_plugin_installer.py` with no arguments opens a window and embeds the newest `.addin` under `src/TiaGitAddIn/bin`. Pass a package path to choose a file from the command line.

To install the package yourself:

1. Copy `src/TiaGitAddIn/bin/Debug/net48/TiaGitAddIn.addin` to `%APPDATA%\Siemens\Automation\Portal V21\UserAddIns`. Create that folder if it does not exist. Do not use `SystemAddIns`.
2. Open TIA Portal V21.
3. Enable the Add-In in the "Add-ins" task card.
4. Right-click a VCI workspace item and choose **Open Git Panel...**.

## What to do next

The summary table under **HMI and hardware in Git** is the list of what export and import cover. **No** is left out. **Partly** is limited in the way the Notes column says. The items below are still open.

1. **Screen review**
   - There is no visual diff of screens or tag tables. Git shows those files as text.
   - A Unified widget is created only when Openness has that type. A type it cannot create stays out of the project.

2. **Items with no export**
   - **WinCC Comfort:** users and groups, scheduled jobs, runtime settings, device settings, and fonts. Fonts set on a screen object travel with that screen. System cycles are exported, and import applies them only when Openness accepts the file.
   - **Unified:** recipes, parameter sets, users, cycles, device settings, and images that are not in the project graphic collection.
   - **Project library:** faceplate types and styles in the project library are exported. Global-library types stay out, and types missing from the folder are not deleted.
   - Unified tag tables, scripts, and lists that are missing from an older `HmiExport` stay in the project. Comfort screens, popups, templates, slide-ins, user tag tables, VB scripts, text lists, connections, and user cycles are removed when their file is missing.

3. **Hardware tree import**
   - Import writes attributes onto devices and modules that are already in the project, and writes node addresses, port links, changed drive parameters, security settings, and drive function settings. It does not create, delete, or move devices or modules.

4. **PLC comparison**
   - Visual diff for **FBD**.
   - Structured **SCL** diff with block-aware syntax highlighting.
   - Block metadata such as Optimized Access.
   - Compare more than one block from the same commit in one view.

5. **Diagram viewer**
   - Cleaner orthogonal wire routing.
   - Zoom and pan in the LAD diff.
   - Follow the TIA Portal theme and system colors.

## Changelog

The add-in version is `AddInVersion` in `src/TiaGitAddIn/AddInPublisherConfiguration.xml`. Newest first.

- **1.2.37** — Export and import show a running timer, and the finished time stays on the checklist.
- **1.2.36** — Export and import cover project pictures, project-library faceplate types and styles, and Comfort system cycles.
- **1.2.35** — The About page shows the add-in version and what export and import cover, including the items they leave out.
- **1.2.34** — The Export page lists the HMI and hardware files stored in the commit selected on History.
- **1.2.33** — Import writes snapshot attributes back onto Unified screens, alarms, connections, logs, runtime settings, Comfort graphic lists and alarms, and the matching hardware devices, drives, and subnets. Widgets are created when Openness has that type.
- **1.2.32** — Unified screen export writes every widget plus the screen and item properties, events, dynamizations, and appearance Openness exposes. Import still restores screen size, buttons, and I/O fields.
- **1.2.31** — Import uses HMI and Hardware checkboxes and one Import button. Hardware import writes node addresses, port links, and changed drive parameters back onto the matching devices.
- **1.2.30** — Hardware export writes each node's IP address, subnet mask, router, PROFINET name, and the port-to-port links.
- **1.2.29** — Settings can show XML changes by element and attribute, or as plain text.
- **1.2.28** — The hardware checklist checks off each row as that part finishes, counting up the same way the HMI checklist does.
- **1.2.27** — Exported XML is compared by element and attribute. Formatting and attribute order no longer show up as changes.
- **1.2.26** — One **Export** button writes the checked items. **HMI** and **Hardware** are separate checkboxes, and both start checked.
- **1.2.25** — The HMI checklist no longer shows a Recipes row. WinCC Unified V21 Openness has no recipe or parameter-set object to export.
- **1.2.24** — Drive export writes only parameters that differ from the factory default, and it reads the accessible parameter list instead of the full catalog.
- **1.2.23** — The Git panel opens larger, sized to the screen, with larger text and a wider diff file list.
- **1.2.22** — Settings fills a blank name and email from the Git identity stored for that repository.
- **1.2.21** — An HMI file keeps the creation time it already has. A file that has none does not gain a new one.
- **1.2.20** — The export checklist is larger, grouped by device, stays open when the export finishes, and no longer repeats the running line above the list.
- **1.2.19** — **Export hardware** writes devices, modules, subnets, and SINAMICS parameters and settings to `HardwareExport/`.
- **1.2.18** — HMI export shows a checklist with counts. Comfort and Unified also export the other compositions Openness exposes. Unified has no recipe composition, which 1.2.25 takes off the checklist.
- **1.2.17** — Unified screens inside screen groups are exported, for example `Screens/Content/`. This install also included 1.2.16.
- **1.2.16** — Unified alarm files include event text and info text for each language.
- **1.2.15** — Comfort alarm and message collections are written as attribute XML.
- **1.2.14** — The Siemens `<Created>` time is removed after export so an unchanged screen stays out of the diff. This install also included 1.2.13.
- **1.2.13** — Unified discrete alarms, analog alarms, and alarm classes are written under `Alarms/`.
- **1.2.12** — Unified tags are exported from each tag table. Siemens does not allow export of the device-level tag list.
- **1.2.11** — Changed HMI files are grouped by device and folder on Status, Diff, and History. This install also included 1.2.10.
- **1.2.10** — Comfort graphic lists are written as attribute XML. Siemens `GraphicList.Export` is not called.
- **1.2.9** — Comfort graphic-list export is skipped. That Siemens call closes TIA Portal.
- **1.2.8** — Graphic lists are exported last. If one fails, the export stops instead of calling the next object.
- **1.2.7** — A graphic list with no name is skipped.
- **1.2.6** — During a Comfort export, TIA's thread does not process other messages. Each step is written to the add-in log.
- **1.2.5** — A Unified device exports tags, scripts, text lists, and graphic lists next to its screens.
- **1.2.4** — The Git panel keeps TIA's thread running, so export is not stuck on "Waiting for TIA Portal".
- **1.2.3** — The progress line names the device and the screen, tag table, or script being written.
- **1.2.2** — The Branch page lists branches. Git's branch output is tab-separated.
- **1.2.1** — The panel can create the Git repository, and History and Branch load when those pages open. A one-time window error no longer closes the panel. The installer copies the package into the TIA user add-ins folder.
- **1.2.0** — The version number moved off 1.0.0 during the LAD work. Status, commit, history, text diff, and graphical LAD comparison were in place. PLC interface comparison, restore, PDF export, and the remote URL in Settings were added in source before the next numbered build.
- **1.0.0** — First packaged add-in.

## Project Structure

```text
installer/             Builds plugin_installer.exe and copies the add-in into UserAddIns
src/
  TiaGitAddIn/
    Entry/               TIA Portal Add-In entry points and menu registration
    UI/                  WPF MVVM (ViewModels, Views, Converters)
    Services/            UI-specific services, HMI device exchange, and add-in implementations
  TiaGitAddIn.Core/
    Models/              Core data models for Git, LAD, HMI export, and Sact/SimaticML
    Services/            Git process execution and repository discovery
    Services/Hmi/        HMI export folder layout and normalization
    Services/SimaticMl/  Native SimaticML parser and comparison engine
    Configuration/       Configuration management
    Logging/             Add-in logging abstractions
  TiaGitAddIn.Tests/     Unit and UI logic tests
```
