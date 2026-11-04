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

TIA Portal VCI cannot contain an HMI device. The Git panel has **HMI** and **Hardware** checkboxes for each direction, with one **Transfer to Git** button and one **Transfer to project** button. Export writes `HmiExport/<device>/` and `HardwareExport/`. Import applies the checked folders back onto the matching devices in the open project. It replaces those objects; it does not merge two edits. Before it writes HMI files, it lists screens and tag tables that differ from the open project and asks you to continue. A checkout only puts the files on disk; **Import** is a separate step.

While an export runs, a checklist stays open. It is grouped by device and shows each category, the item being written, and the count when that category finishes. Export keeps a creation time that is already stored in a file, and leaves a new one out when the file had none, so an unchanged device does not rewrite every file.

**Yes** means that item is written and read back. **Partly** means the file or the write-back is limited; the limit is in Notes. **No** means that item is left out. Where Notes do not say otherwise, **Yes** is a Siemens export or import, and **Partly** is a snapshot of the attributes and child objects Openness exposes. The import checkboxes start unchecked.

| Area | Item | Export | Import | Notes |
| --- | --- | --- | --- | --- |
| Unified | Screens and every widget Openness exposes | Partly | Partly | Screen properties, events, dynamizations, and appearance. Buttons and I/O fields store text and process value. A widget is created only when Openness has that type. Widgets missing from the file are removed |
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
| Comfort | Graphic lists | Partly | Partly | Attribute snapshot, including list entries. Siemens `GraphicList.Export` is not called |
| Comfort | Alarms and messages | Partly | Partly | Compositions whose name contains "alarm" or "message" |
| Comfort | Other compositions Openness lists | Partly | Partly | Attribute snapshot, plus a Siemens graphic file when the composition exports one |
| Comfort | Recipes | Partly | Partly | Only when Openness lists a recipe composition. The V21 public API has no recipe type |
| Comfort | Users, groups, scheduled jobs, runtime settings, device settings, fonts | No | No | Fonts set on a screen object travel with that screen |
| Project | Project pictures | Yes | Yes | Image files are included. Pictures missing from the folder are removed |
| Project | Faceplate types in the project library | Yes | Partly | The in-work version is used when one exists. A missing type is created. Types missing from the folder stay. Global libraries are left out |
| Project | Styles and style sheets in the project library | Yes | Partly | Same limits as faceplate types |
| Hardware | Device and module tree, type, rack position, PLC I/O addresses | Yes | Partly | Type, rack position, and I/O addresses are written. Other number and enum attributes are left out, because a null number closes TIA Portal. Devices and modules are not created, deleted, or moved |
| Hardware | Subnet name, type, and IO system | Yes | Yes | |
| Hardware | Node IP address, subnet mask, router, PROFINET name | Yes | Yes | Written when Openness provides the value |
| Hardware | Port-to-port links | Yes | Yes | |
| Hardware | SINAMICS parameters that differ from the factory default | Yes | Yes | |
| Hardware | SINAMICS security settings | Yes | Yes | Text attributes. Number and enum attributes are left out |
| Hardware | SINAMICS telegram type | Yes | Partly | Written when Openness accepts the change |
| Hardware | Drive function settings | Yes | Yes | Function settings and technology extensions on the matching module. Number and enum attributes are left out |
| PLC | PLC blocks | Yes | No | TIA Portal VCI writes the files. There is no PLC import button. **Restore** checks the commit out on a `restore-<short hash>` branch |

Pictures, faceplate types, styles, and style sheets are written to `HmiExport/_Project/<project>/` and imported before the HMI devices, so a screen can find its faceplate type. A faceplate placed on a screen is part of that screen's widget snapshot. `HardwareExport/` includes devices that are not in a group. PLC blocks stay on the VCI path.

### TIA Portal V21 Compare API Decision

TIA Portal V21's supported Public API includes in-process, data-only comparison for TIA engineering objects (`PlcSoftware.CompareTo`, `PlcSoftware.CompareToOnline`, hardware/library comparisons) and coarse VCI mapped-object status. The engineering-object methods return comparison result trees; VCI exposes status data only. Neither surface provides Siemens' graphical LAD/FBD comparison editor or accepts Git/SimaticML revision files. The V21 Add-In API exposes no supported compare/diff UI entry point.

Git revision review therefore continues to use the project-owned native SimaticML comparer and custom LAD renderer, with the text/structured view as fallback. Internal `Siemens.Automation.CommonServices.Compare.*` UI and command types are not used. See the [V21 compare API investigation](docs/tia-v21-compare-api-investigation.md) for the evidence and decision matrix.

### Git Integration

- **VCI-Aware**: Uses the selected TIA Portal VCI workspace as the Git working tree.
- **First use**: If that folder is not a Git repository yet, the add-in asks to create one there. You do not need to open a command prompt.
- **Core Operations**: View status, history, branches, and commit changes without leaving TIA Portal. The History and Branch pages load when you open them. **Show changes** on Status compares the current workspace files with the last commit. Files can be staged when the workspace path contains spaces.
- **Export page**: Select a commit on History. The Export page lists the HMI and hardware files stored in that commit, grouped by device and folder.
- **About page**: Shows the add-in version and the same coverage as the table above, with the export and import wording spelled out. Shaded rows are left out of export, import, or both.
- **Text diff navigation**: In a text diff, **Previous change** and **Next change** scroll to the previous and next changed block.
- **Restore**: Restoring a commit creates a branch named `restore-<short hash>` and checks it out. The original branch is left unchanged.
- **Commit identity**: In Settings, **Your Name** and **Your Email** are saved into the repository so commits made from TIA Portal have an author. When those boxes are empty, Settings fills them from the name and email already stored for this repository.
- **Transparent Execution**: Uses your local `git.exe`. It is found on `PATH`, or at `C:\Program Files\Git\cmd\git.exe`.
- **Remote repository**: In Settings, set **Default Remote** (usually `origin`) and **Remote URL**, then save. The URL can be a GitHub address (`https://github.com/org/repo.git` or `git@github.com:org/repo.git`) or an Azure DevOps address (`https://dev.azure.com/org/project/_git/repo`, a `visualstudio.com` URL, an Azure DevOps Server URL containing `/_git/`, or `git@ssh.dev.azure.com:v3/org/project/repo`). Fetch, Pull, and Push use that remote name. Leave the password out of the URL. Sign in once with `git fetch` from a command prompt in the workspace folder so Git Credential Manager can store the credentials. If a push is rejected because those credentials are missing, the panel says to run that same command.
- **PDF export**: History puts the loaded commits on the first page and the changed files of each commit on the following pages, under their folders. Diff exports the changed-file list and the open comparison. A ladder is written as networks and instructions, and a screen lists the highlighted buttons and I/O fields. The ladder drawing itself is not pasted in.

Exported XML, including HMI and hardware files, is compared by element and attribute. A changed value is shown on its own line, such as `Item[@name="Start"]/@text`. Line breaks and the order of attributes are ignored. A screen file draws buttons and I/O fields as boxes, with added, removed, and moved items marked. In Settings, **Screen** can switch that drawing back to the XML text. A tag table shows name, address, data type, and comment above the line diff. In Settings, **XML changes** can switch that view back to plain text, which shows the file line by line. A PLC tag table shows name, address, data type, retain, and comment. A PLC data type lists its members. In Settings, **PLC tags and data types** can switch that grid back to the XML text. An SCL block lists the declarations and statements that changed. The colored text stays on the Raw text tab, and a file that is not an SCL block still shows added lines in green and removed lines in red. In Settings, **SCL** chooses which of those opens first. In Settings, **Whitespace** follows Git's diff options. **Ignore all white space (-w)** starts checked, so a line that differs only by spaces is not a change. The other boxes ignore tab expansion, trailing spaces, a change in the amount of white space, or a blank line. **Ignore comments** starts unchecked. When it is checked, a `//` comment or a comment between `(*` and `*)` (or `*(` and `*)`) is not a change. A line that was inserted no longer marks every line below it. Save stores every setting in this workspace. The note that semantic comparison is unavailable means that file is being compared as text. LAD blocks that can be parsed use the graphical comparison instead.

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

To look at the panel without starting TIA Portal, run `python open_panel.py` from this folder. Pass another folder to open that Git workspace instead. Commit, fetch, and the other Git buttons act on that folder. HMI and hardware import and export still need the panel opened from TIA Portal.

### Installation

The repository includes `installer/out/plugin_installer.exe`. That file embeds a Release add-in from when it was last built, so it can be run without compiling. It checks that the destination is the TIA Portal V21 `UserAddIns` folder. If that folder is missing or is not the right one, you can browse to `Portal V21\UserAddIns`. It then copies the package there, tells you which add-in version was installed, and lists the steps to enable it in the Add-ins task card. It also runs `git.exe` and shows the path when Git works. If Git is missing or that file does not run, it offers to open the download page. Close TIA Portal first if the add-in file is already in use.

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

The table under **HMI and hardware in Git** is the list of what export and import cover. The items below are still open and are not already stated there.

1. **Screen review.** The Diff tab draws buttons and I/O fields from an exported screen. Settings can show the XML text instead. A tag table shows name, address, data type, and comment. Other widgets, faceplates, and graphics stay in the text diff.
2. **PLC comparison.** Visual diff for **FBD**. Block metadata such as Optimized Access. GRAPH and STL stay as text.
3. **Diagram viewer.** Cleaner orthogonal wire routing. The LAD view can zoom, jump to a network, and fit the width. Panning is the scroll bars. The panel follows the Windows app theme. It does not read TIA Portal's own theme.

## Changelog

The add-in version is `AddInVersion` in `src/TiaGitAddIn/AddInPublisherConfiguration.xml`. Newest first.

- **1.2.68** — An HMI tag table such as hcm software versions fills the comparison as a grid of tag names, instead of long XML paths in a short strip.
- **1.2.67** — Hovering a highlighted screen item names the caption, position, size, or tag that changed, and the same text stays under the zoom buttons.
- **1.2.66** — A PLC tag table colors added and changed rows and can jump to the next one. A screen drawing uses the full comparison height, and hovering a highlighted item says what changed.
- **1.2.65** — An SCL declaration keeps its `{ ... }` attributes, so a change such as ExternalAccessible is shown in the structure and not only in the raw text.
- **1.2.64** — Settings can open an SCL block on its structure or on the colored lines. Both views stay available on the diff.
- **1.2.63** — An SCL block is compared as its declarations and statements, with the colored text still on the Raw text tab. Diff can review the SCL blocks in one commit and jump to the next change. File history is one line per version. A PLC tag table or data type opens as a grid. Pull shows the incoming commits and PLC files before it runs. A conflicted file can show both sides, and any two commits can be compared.
- **1.2.62** — File history no longer draws the open comparison on top of the version list. The History page uses the full width for changed files.
- **1.2.61** — Show history on an open diff lists every version of that file in one view, newest at the top. Each block is the comparison for that commit.
- **1.2.60** — Diff and History can step through the commits that changed one file, or a set of files you choose. Previous version and Next version open that comparison, newest first.
- **1.2.59** — The history PDF lists the commits on the first page, then the changed files of every commit on the following pages.
- **1.2.58** — PDF export includes ladder networks and highlighted screen items, keeps letters such as ä, and numbers the pages. History lists changed files under their folders.
- **1.2.57** — A changed file whose name Git quotes, such as a block name with a space or an accent, keeps its path and can be compared.
- **1.2.56** — Changed PLC blocks are listed under the software unit and folder they belong to, instead of one flat list.
- **1.2.55** — Transfer progress opens in its own window. Minimize it and keep using the other Git panel pages while the transfer runs.
- **1.2.54** — A screen button or I/O field is highlighted when the tag or screen it uses changes, even if the caption and position stay the same. The screen drawing has −, +, and 100% zoom.
- **1.2.53** — Raw text has Previous change and Next change, and they scroll to the colored block. Diff trees such as Hardware and HMI start collapsed.
- **1.2.52** — Stage all no longer stops Git after 30 seconds. Stopping it there closed TIA Portal. A Git command that fails is written to the add-in log instead of closing Portal.
- **1.2.51** — Screen comparison draws added, removed, and changed items with a strong color and a thick border, and fades items that did not change. The Raw text tab colors a line red when it is only in Previous and green when it is only in Current.
- **1.2.50** — Transfer to Git reads each hardware object's text attributes once, skips drive devices while looking for HMI panels, and reads a drive parameter's other columns only when the value is not the factory value. A Siemens error on one device, module, drive, or screen is listed at the end in text that can be copied, and the rest of the transfer still runs.
- **1.2.49** — A hardware module that Openness cannot read is skipped. One such module no longer stops Transfer to Git with "One or more errors occurred."
- **1.2.48** — Hardware export no longer reads number and enum attributes in one bulk call. A null number makes TIA Portal exit, and the add-in cannot catch that. Text attributes are still written. Addresses, telegrams, and drive parameters keep their own numbers.
- **1.2.47** — Settings can ignore `//` comments and comments between `(*` and `*)` or `*(` and `*)`. That box starts unchecked. Save keeps every setting, including the whitespace boxes.
- **1.2.46** — Settings lists Git's whitespace options for a text diff, with ignore-all-space (-w) on by default. A line that was inserted no longer marks every line below it.
- **1.2.45** — Hardware export walks the module tree once, and drive function settings are read only on modules that have a drive. Unified screens are read in one pass.
- **1.2.44** — Drive parameter export reads each parameter once and keeps the checklist moving while factory defaults are skipped. Safety settings are labeled after the telegrams.
- **1.2.43** — Transfer to Git also reads a project opened as a multiuser local session, and the error names the project path TIA reports.
- **1.2.42** — Hardware transfer also reads devices in Ungrouped devices and in device groups, not only devices at the project root.
- **1.2.41** — A failed Transfer to Git or Transfer to project opens an error dialog, and the same text stays under the buttons.
- **1.2.40** — The Changes page buttons are Transfer to Git and Transfer to project.
- **1.2.39** — The Diff tab draws buttons and I/O fields and lists tag-table rows. Settings can show a screen as that drawing or as XML text. Import lists screens and tag tables that differ from the open project before it replaces them. A push that cannot sign in says to run `git fetch` once in the workspace folder.
- **1.2.38** — The Changes page holds commit, export, and import. A file opens in the diff, and Settings can leave HMI and hardware categories out of an export.
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
