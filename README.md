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

TIA Portal VCI cannot contain an HMI device. The Git panel has **Export HMI** and **Import HMI** for the open Unified or WinCC Comfort device (panel or runtime). Export writes `HmiExport/<device>/` in the Git working tree. Import applies that folder back onto the matching open device after a checkout.

Unified export includes screens, including screens inside screen groups, tag tables and tag groups, JavaScript modules, text lists, graphic lists, and discrete alarms, analog alarms, and alarm classes. Alarm files include event text and info text, so those edits show up in Git. Unified import restores screens, tags, scripts, text lists, and graphic lists.

WinCC Comfort export includes screens and screen folders, popups, templates, slide-in screens, tag tables, VB scripts, text lists, graphic lists, connections, cycles, the permanent area, the overview, and alarms. Comfort import restores screens, popups, templates, slide-in screens, tag tables, VB scripts, text lists, connections, cycles, the permanent area, and the overview.

Export strips creation timestamps so an unchanged device does not rewrite every file. Alarm files stay in Git for review; import does not write them back into the device.

### TIA Portal V21 Compare API Decision

TIA Portal V21's supported Public API includes in-process, data-only comparison for TIA engineering objects (`PlcSoftware.CompareTo`, `PlcSoftware.CompareToOnline`, hardware/library comparisons) and coarse VCI mapped-object status. The engineering-object methods return comparison result trees; VCI exposes status data only. Neither surface provides Siemens' graphical LAD/FBD comparison editor or accepts Git/SimaticML revision files. The V21 Add-In API exposes no supported compare/diff UI entry point.

Git revision review therefore continues to use the project-owned native SimaticML comparer and custom LAD renderer, with the text/structured view as fallback. Internal `Siemens.Automation.CommonServices.Compare.*` UI and command types are not used. See the [V21 compare API investigation](docs/tia-v21-compare-api-investigation.md) for the evidence and decision matrix.

### Git Integration

- **VCI-Aware**: Uses the selected TIA Portal VCI workspace as the Git working tree.
- **First use**: If that folder is not a Git repository yet, the add-in asks to create one there. You do not need to open a command prompt.
- **Core Operations**: View status, history, branches, and commit changes without leaving TIA Portal. The History and Branch pages load when you open them. **Show changes** on Status compares the current workspace files with the last commit. Files can be staged when the workspace path contains spaces.
- **Text diff navigation**: In a text diff, **Previous change** and **Next change** scroll to the previous and next changed block.
- **Restore**: Restoring a commit creates a branch named `restore-<short hash>` and checks it out. The original branch is left unchanged.
- **Commit identity**: In Settings, **Your Name** and **Your Email** are saved into the repository so commits made from TIA Portal have an author.
- **Transparent Execution**: Uses your local `git.exe`. It is found on `PATH`, or at `C:\Program Files\Git\cmd\git.exe`.
- **Remote repository**: In Settings, set **Default Remote** (usually `origin`) and **Remote URL**, then save. The URL can be a GitHub address (`https://github.com/org/repo.git` or `git@github.com:org/repo.git`) or an Azure DevOps address (`https://dev.azure.com/org/project/_git/repo`, a `visualstudio.com` URL, an Azure DevOps Server URL containing `/_git/`, or `git@ssh.dev.azure.com:v3/org/project/repo`). Fetch, Pull, and Push use that remote name. Leave the password out of the URL. Sign in once with `git fetch` from a command prompt in the workspace folder so Git Credential Manager can store the credentials.
- **PDF export**: History exports the loaded commits and, when a commit is selected, its changed files. Diff exports the changed-file list and the open text or interface comparison. Ladder diagrams are left out of the PDF.

Text diffs for SCL, generic XML, and plain text files show added lines in green and removed lines in red. The note that semantic comparison is unavailable means the add-in is comparing those files as text. LAD blocks that can be parsed use the graphical comparison instead.

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

Export and import of Unified and WinCC Comfort devices is already in the Git panel. The items below are the remaining gaps.

1. **HMI attributes still missing**
   - **WinCC Comfort:** recipes, data logs, alarm logs, users and groups, scheduled jobs, runtime settings, device settings, the image library, and fonts. System cycles are skipped. Graphic lists and alarms are attribute snapshots, and the alarm pass only keeps Openness compositions whose name contains "alarm" or "message".
   - **Unified:** connections, cycles, logging, parameter sets, users, runtime settings, device settings, images, styles, and faceplates. A screen file stores the name, screen-group folder, width, and height, plus buttons and I/O fields. Other widgets, events, animations, appearance, and the remaining screen properties are missing. Alarm attributes are one level deep, plus event text and info text.
   - Import does not yet delete Comfort or Unified objects that are absent from an older `HmiExport`, except for the Comfort types that already use Siemens override. A checkout restores the files on disk; **Import HMI** is still a separate step.

2. **Drives and parameters**
   - Drives belong to the PLC or a drive device, not to the HMI export. Read Startdrive / SINAMICS objects and their parameters into Git through the Openness hardware API.

3. **Hardware configuration**
   - Export and compare devices, modules, and networks. The V21 compare API returns data only and does not provide a hardware diff editor, so this stays a project-owned export.

4. **PLC comparison**
   - Visual diff for **FBD**.
   - Structured **SCL** diff with block-aware syntax highlighting.
   - Block metadata such as Optimized Access.
   - Compare more than one block from the same commit in one view.
   - Visual diff for the HMI screens and tag tables once their export is complete.

5. **Diagram viewer**
   - Cleaner orthogonal wire routing.
   - Zoom and pan in the LAD diff.
   - Follow the TIA Portal theme and system colors.

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
