"""Build plugin_installer.exe by embedding the latest TIA Git .addin file.

The script compiles installer/PluginInstaller.cs and appends the plugin
package to that program. Running the resulting exe checks the TIA Portal
user add-ins folder, lets you browse to it when that folder is not the
Portal UserAddIns folder, and then copies the package there:

    %APPDATA%\\Siemens\\Automation\\Portal V21\\UserAddIns

Starting the script with no arguments opens a window. Pass a plugin path to
build from the command line instead.

Examples:
    python installer/build_plugin_installer.py
    python installer/build_plugin_installer.py C:\\builds\\TiaGitAddIn.addin
    python installer/build_plugin_installer.py --output D:\\plugin_installer.exe
"""

from __future__ import annotations

import argparse
import struct
import subprocess
import sys
from pathlib import Path
from typing import List, Optional


MAGIC = b"TGADDIN1"


class BuildError(Exception):
    """A problem the user can fix by changing the command or the plugin file."""


def main(argv: Optional[List[str]] = None) -> int:
    if argv is None and len(sys.argv) == 1:
        return launch_ui()

    parser = argparse.ArgumentParser(
        description="Embed the latest TIA Git .addin file in plugin_installer.exe."
    )
    parser.add_argument(
        "addin",
        nargs="?",
        help="Path to the .addin package. Defaults to the newest file under src/TiaGitAddIn/bin.",
    )
    parser.add_argument(
        "--output",
        help="Path of plugin_installer.exe. Defaults to installer/out/plugin_installer.exe.",
    )
    parser.add_argument(
        "--portal-version",
        type=int,
        default=21,
        help="TIA Portal major version used in the install folder name. Default: 21.",
    )
    args = parser.parse_args(argv)

    try:
        if args.portal_version < 1 or args.portal_version > 99:
            raise BuildError("--portal-version must be from 1 to 99.")
        build(addin=args.addin, output=args.output, portal_version=args.portal_version)
    except BuildError as exc:
        print(exc, file=sys.stderr)
        return 1
    return 0


def compile_installer(installer_dir: Path) -> Path:
    project = installer_dir / "PluginInstaller.csproj"
    completed = subprocess.run(
        ["dotnet", "build", str(project), "-c", "Release", "--nologo"],
        cwd=installer_dir.parent,
        capture_output=True,
        text=True,
    )
    exe = installer_dir / "bin" / "Release" / "PluginInstaller.exe"
    if completed.returncode != 0 or not exe.is_file():
        detail = (completed.stderr or completed.stdout or "").strip()
        raise BuildError("Could not compile the installer.\n" + detail)
    return exe


def build(addin: Optional[str], output: Optional[str], portal_version: int) -> None:
    installer_dir = Path(__file__).resolve().parent
    stub_path = compile_installer(installer_dir)

    addin_path = resolve_addin_path(installer_dir.parent, addin)
    if addin_path.stat().st_size <= 0:
        raise BuildError(f"The add-in file is empty: {addin_path}")
    if addin_path.suffix.lower() != ".addin":
        raise BuildError(f"Expected a .addin file, got: {addin_path.name}")

    name = addin_path.name.encode("utf-8")
    if not name or len(name) > 65535:
        raise BuildError(f"The add-in file name cannot be embedded: {addin_path.name}")

    output_path = (
        Path(output).expanduser().resolve()
        if output
        else installer_dir / "out" / "plugin_installer.exe"
    )
    output_path.parent.mkdir(parents=True, exist_ok=True)

    payload = addin_path.read_bytes()
    footer = struct.pack("<HHQ", len(name), portal_version, len(payload)) + MAGIC
    output_path.write_bytes(stub_path.read_bytes() + payload + name + footer)
    verify_embedded(output_path, payload, addin_path.name, portal_version)

    print(f"Package: {addin_path}")
    print(f"Created {output_path}")
    print(f"Size    {format_byte_count(output_path.stat().st_size)}")
    print(
        "Running the exe copies the plugin to "
        f"%APPDATA%\\Siemens\\Automation\\Portal V{portal_version}\\UserAddIns"
    )


def list_addin_files(repo_root: Path) -> List[Path]:
    bin_root = repo_root / "src" / "TiaGitAddIn" / "bin"
    if not bin_root.is_dir():
        return []
    found = [item for item in bin_root.rglob("*.addin") if item.is_file()]
    found.sort(key=lambda item: item.stat().st_mtime, reverse=True)
    return found


def resolve_addin_path(repo_root: Path, requested: Optional[str]) -> Path:
    if requested:
        path = Path(requested).expanduser()
        if not path.is_file():
            raise BuildError(f"Add-in file not found: {requested}")
        return path.resolve()

    found = list_addin_files(repo_root)
    if not found:
        raise BuildError(
            "No .addin file found under src\\TiaGitAddIn\\bin.\n\n"
            "Build the plugin, then run this script again:\n"
            "  dotnet build src\\TiaGitAddIn\\TiaGitAddIn.csproj\n\n"
            "Or pass the package directly:\n"
            "  python installer/build_plugin_installer.py C:\\path\\TiaGitAddIn.addin"
        )
    if len(found) > 1:
        print("Several .addin files were found. Using the newest.")
    return found[0].resolve()


def verify_embedded(output_path: Path, payload: bytes, file_name: str, portal_version: int) -> None:
    data = output_path.read_bytes()
    if len(data) < 20 or data[-8:] != MAGIC:
        raise BuildError("The installer exe does not contain the plugin footer.")

    name_len, embedded_portal, payload_len = struct.unpack_from("<HHQ", data, len(data) - 20)
    name_end = len(data) - 20
    name_start = name_end - name_len
    payload_end = name_start
    payload_start = payload_end - payload_len
    if payload_start < 0 or name_start < 0:
        raise BuildError("The embedded plugin package is truncated.")

    embedded_name = data[name_start:name_end].decode("utf-8")
    embedded_payload = data[payload_start:payload_end]
    if embedded_name != file_name or embedded_portal != portal_version or embedded_payload != payload:
        raise BuildError("The embedded plugin does not match the source file.")


def launch_ui() -> int:
    import os
    from datetime import datetime

    enable_windows_dpi()
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk
    installer_dir = Path(__file__).resolve().parent
    repo_root = installer_dir.parent
    default_output = installer_dir / "out" / "plugin_installer.exe"
    packages = list_addin_files(repo_root)

    root = tk.Tk()
    root.title("Build TIA Git Add-In Installer")
    root.resizable(False, False)
    style = ttk.Style()
    if "vista" in style.theme_names():
        style.theme_use("vista")
    background = style.lookup("TFrame", "background") or "SystemButtonFace"

    frame = ttk.Frame(root, padding=(24, 20, 24, 16))
    frame.grid(sticky="nsew")
    frame.columnconfigure(0, weight=1)

    ttk.Label(frame, text="Build plugin installer", font=("Segoe UI", 16, "bold")).grid(
        row=0, column=0, columnspan=2, sticky="w"
    )
    ttk.Label(
        frame,
        text="Embeds a .addin file in plugin_installer.exe. Running that exe checks the TIA Portal user add-ins folder, lets you browse if it is not the right folder, and copies the plugin there.",
        wraplength=560,
        justify="left",
    ).grid(row=1, column=0, columnspan=2, sticky="w", pady=(6, 0))

    ttk.Label(frame, text="Plugin file").grid(row=2, column=0, columnspan=2, sticky="w", pady=(18, 4))
    addin_var = tk.StringVar(value=str(packages[0]) if packages else "")
    ttk.Entry(frame, textvariable=addin_var, width=72).grid(row=3, column=0, sticky="ew")
    ttk.Button(frame, text="Browse...", command=lambda: browse_addin(addin_var)).grid(
        row=3, column=1, padx=(8, 0)
    )

    package_text = tk.StringVar()
    package_label = tk.Label(
        frame,
        textvariable=package_text,
        anchor="w",
        justify="left",
        bg=background,
        fg="#5a6268",
        font=("Segoe UI", 9),
    )
    package_label.grid(row=4, column=0, columnspan=2, sticky="w", pady=(4, 0))

    ttk.Label(frame, text="Installer output").grid(row=5, column=0, columnspan=2, sticky="w", pady=(14, 4))
    output_var = tk.StringVar(value=str(default_output))
    ttk.Entry(frame, textvariable=output_var, width=72).grid(row=6, column=0, sticky="ew")
    ttk.Button(frame, text="Browse...", command=lambda: browse_output(output_var, default_output)).grid(
        row=6, column=1, padx=(8, 0)
    )

    ttk.Label(frame, text="TIA Portal version").grid(row=7, column=0, sticky="w", pady=(14, 4))
    portal_var = tk.StringVar(value="21")
    ttk.Spinbox(frame, from_=1, to=99, textvariable=portal_var, width=8).grid(row=8, column=0, sticky="w")

    destination_text = tk.StringVar()
    tk.Label(
        frame,
        textvariable=destination_text,
        anchor="w",
        justify="left",
        bg=background,
        fg="#5a6268",
        font=("Segoe UI", 9),
    ).grid(row=9, column=0, columnspan=2, sticky="w", pady=(4, 0))

    status_text = tk.StringVar(value="Ready.")
    status_label = tk.Label(
        frame,
        textvariable=status_text,
        anchor="w",
        justify="left",
        wraplength=560,
        bg=background,
        fg="#465056",
        font=("Segoe UI", 9),
    )
    status_label.grid(row=10, column=0, columnspan=2, sticky="w", pady=(16, 8))

    buttons = ttk.Frame(frame)
    buttons.grid(row=11, column=0, columnspan=2, sticky="e")
    create_button = ttk.Button(buttons, text="Create installer", command=lambda: create_installer())
    create_button.grid(row=0, column=0, padx=(0, 8))
    ttk.Button(buttons, text="Open folder", command=lambda: open_output_folder()).grid(row=0, column=1, padx=(0, 8))
    ttk.Button(buttons, text="Close", command=root.destroy).grid(row=0, column=2)

    def refresh_package(*_args: object) -> None:
        raw = addin_var.get().strip()
        if not raw:
            package_text.set("Choose a .addin file, or build the plugin first.")
            return
        path = Path(raw)
        if not path.is_file():
            package_text.set("File not found.")
            return
        modified = datetime.fromtimestamp(path.stat().st_mtime).strftime("%Y-%m-%d %H:%M")
        package_text.set(f"{path.name}   ·   {format_byte_count(path.stat().st_size)}   ·   {modified}")

    def refresh_destination(*_args: object) -> None:
        version = portal_var.get().strip() or "21"
        destination_text.set(
            f"The exe installs into %APPDATA%\\Siemens\\Automation\\Portal V{version}\\UserAddIns"
        )

    def set_status(text: str, color: str) -> None:
        status_text.set(text)
        status_label.configure(fg=color)

    def create_installer() -> None:
        raw_version = portal_var.get().strip()
        try:
            portal_version = int(raw_version)
        except ValueError:
            set_status("TIA Portal version must be a number from 1 to 99.", "#a4262c")
            messagebox.showerror("Could not create the installer", "TIA Portal version must be a number from 1 to 99.")
            return
        chosen = addin_var.get().strip()
        if not chosen:
            set_status("Choose a plugin .addin file.", "#a4262c")
            messagebox.showerror("Could not create the installer", "Choose a plugin .addin file.")
            return
        output = output_var.get().strip()
        create_button.state(["disabled"])
        set_status("Creating the installer...", "#465056")
        root.update_idletasks()
        try:
            build(addin=chosen, output=output or None, portal_version=portal_version)
        except BuildError as exc:
            set_status(str(exc), "#a4262c")
            messagebox.showerror("Could not create the installer", str(exc))
            return
        finally:
            create_button.state(["!disabled"])

        created = Path(output).expanduser().resolve() if output else default_output
        summary = (
            f"Created {created}\n"
            f"Size {format_byte_count(created.stat().st_size)}\n\n"
            "Running the exe copies the plugin into the TIA Portal user add-ins folder."
        )
        set_status(summary.replace("\n\n", "\n"), "#007268")
        messagebox.showinfo("Installer created", summary)

    def open_output_folder() -> None:
        raw = output_var.get().strip()
        folder = (Path(raw).expanduser().resolve().parent if raw else default_output.parent)
        if not folder.is_dir():
            messagebox.showinfo("Output folder", "The output folder does not exist yet. It is created when you build the installer.")
            return
        os.startfile(folder)  # type: ignore[attr-defined]

    def browse_addin(target: tk.StringVar) -> None:
        selected = filedialog.askopenfilename(
            title="Select the plugin file",
            filetypes=[("TIA add-in", "*.addin"), ("All files", "*.*")],
        )
        if selected:
            target.set(selected)

    def browse_output(target: tk.StringVar, initial: Path) -> None:
        selected = filedialog.asksaveasfilename(
            title="Save the installer as",
            initialdir=str(initial.parent),
            initialfile=initial.name,
            defaultextension=".exe",
            filetypes=[("Executable", "*.exe")],
        )
        if selected:
            target.set(selected)

    if len(packages) > 1:
        set_status("Several plugin files were found. The newest one is selected.", "#8c5a00")
    addin_var.trace_add("write", refresh_package)
    portal_var.trace_add("write", refresh_destination)
    refresh_package()
    refresh_destination()
    root.update_idletasks()
    width = root.winfo_width()
    height = root.winfo_height()
    root.geometry(f"+{(root.winfo_screenwidth() - width) // 2}+{(root.winfo_screenheight() - height) // 2}")
    create_button.focus_set()
    root.mainloop()
    return 0


def enable_windows_dpi() -> None:
    if sys.platform != "win32":
        return
    try:
        import ctypes

        ctypes.windll.shcore.SetProcessDpiAwareness(1)
    except Exception:
        try:
            import ctypes

            ctypes.windll.user32.SetProcessDPIAware()
        except Exception:
            return


def format_byte_count(size: int) -> str:
    if size < 1024:
        return f"{size} B"
    if size < 1024 * 1024:
        return f"{size / 1024:.1f} KB"
    return f"{size / 1024 / 1024:.2f} MB"


if __name__ == "__main__":
    sys.exit(main())
