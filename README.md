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

### HMI in Git

TIA Portal VCI cannot contain an HMI device. The Git panel has **HMI** and **Hardware** checkboxes and one **Export** button. **Import HMI** applies `HmiExport/<device>/` back onto the matching open Unified or WinCC Comfort device (panel or runtime) after a checkout. HMI export writes that same folder.

While an export runs, a checklist stays open. It is grouped by device and shows each category, the item being written, and the count when that category finishes.

Unified export includes screens, including screens inside screen groups, tag tables and tag groups, system tags, plant-object tags, JavaScript modules, text lists, system text lists, graphic lists, discrete alarms, analog alarms, alarm classes, OPC UA alarm types, and alarm audit classes. Alarm files include event text and info text. It also writes connections, data logs, alarm logs, audit trails, and runtime settings. Any other composition that is not already covered is written under `Other/`. WinCC Unified V21 Openness does not expose recipes or parameter sets, so those are not exported. A Unified screen file stores the name, screen-group folder, width, and height, plus buttons and I/O fields. Unified import restores screens, tags, scripts, text lists, system text lists, and graphic lists.

WinCC Comfort export includes screens and screen folders, popups, templates, slide-in screens, tag tables, VB scripts, text lists, connections, user cycles, the permanent area, and the overview. System cycles are skipped. Graphic lists and alarms are attribute snapshots, and the alarm pass only keeps compositions whose name contains "alarm" or "message". Graphics and any further composition Openness lists are written the same way. A recipe composition is written only when Openness actually lists one. The V21 public API has no recipe type. Comfort import restores screens, popups, templates, slide-in screens, tag tables, VB scripts, text lists, connections, cycles, the permanent area, and the overview.

Export keeps a creation time that is already stored in a file, and leaves a new one out when the file had none, so an unchanged device does not rewrite every file. Alarm files and the other snapshots stay in Git for review. Import does not write them back into the device.

### Hardware in Git

With **Hardware** checked, **Export** writes `HardwareExport/` for the devices in the project, including devices that are not in a group. Each device gets `Hardware.xml` with its module tree, type, rack position, and PLC I/O addresses. SINAMICS files under `Drives/` contain telegrams, security settings, and only the parameters whose value differs from the factory default. Drive function settings are written under `Settings/`. Subnets are written with their name, type, and the names of their nodes and IO systems. IP addresses, subnet masks, routers, PROFINET names, and port-to-port wiring are not in that file. PLC blocks stay on the VCI path. There is no hardware import.

### TIA Portal V21 Compare API Decision

TIA Portal V21's supported Public API includes in-process, data-only comparison for TIA engineering objects (`PlcSoftware.CompareTo`, `PlcSoftware.CompareToOnline`, hardware/library comparisons) and coarse VCI mapped-object status. The engineering-object methods return comparison result trees; VCI exposes status data only. Neither surface provides Siemens' graphical LAD/FBD comparison editor or accepts Git/SimaticML revision files. The V21 Add-In API exposes no supported compare/diff UI entry point.

Git revision review therefore continues to use the project-owned native SimaticML comparer and custom LAD renderer, with the text/structured view as fallback. Internal `Siemens.Automation.CommonServices.Compare.*` UI and command types are not used. See the [V21 compare API investigation](docs/tia-v21-compare-api-investigation.md) for the evidence and decision matrix.

### Git Integration

- **VCI-Aware**: Uses the selected TIA Portal VCI workspace as the Git working tree.
- **First use**: If that folder is not a Git repository yet, the add-in asks to create one there. You do not need to open a command prompt.
- **Core Operations**: View status, history, branches, and commit changes without leaving TIA Portal. The History and Branch pages load when you open them. **Show changes** on Status compares the current workspace files with the last commit. Files can be staged when the workspace path contains spaces.
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

Export and import of Unified and WinCC Comfort devices, and export of the hardware tree and changed drive parameters, are already in the Git panel. The items below are the remaining gaps.

1. **HMI screen content**
   - A Unified screen file stores the name, screen-group folder, width, and height, plus buttons and I/O fields. Other widgets, events, animations, appearance, and the remaining screen properties are missing.
   - There is no visual diff of screens or tag tables. Git shows those files as text.

2. **HMI items the export still does not cover**
   - **WinCC Comfort:** users and groups, scheduled jobs, runtime settings, device settings, fonts, picture files, and recipes. The V21 public API has no recipe type, so a recipe is exported only when Openness lists that composition. System cycles are skipped. Graphic lists, alarms, and graphics are attribute snapshots, so Import HMI does not load them back.
   - **Unified:** recipes and parameter sets, users, cycles, device settings, images, styles, and faceplates. V21 Openness has no recipe or parameter-set object. Connections, logs, runtime settings, and the extra compositions are already exported as attribute XML, and Import HMI does not load those files back.
   - Import does not yet delete Comfort or Unified objects that are absent from an older `HmiExport`, except for the Comfort types that already use Siemens override. A checkout restores the files on disk; **Import HMI** is still a separate step.

3. **Network addresses**
   - Hardware export stores the subnet name, type, and node names. IP address, subnet mask, router, PROFINET device name, and port-to-port wiring are still missing. There is no hardware import.

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
