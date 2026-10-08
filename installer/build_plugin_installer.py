"""Build plugin_installer.exe by embedding the latest TIA Git .addin file.

This script does not compile anything. It appends the plugin package to
installer/plugin_installer_stub.exe. Running the resulting exe copies that
package into the TIA Portal user add-ins folder:

    %APPDATA%\\Siemens\\Automation\\Portal V21\\UserAddIns

Examples:
    python installer/build_plugin_installer.py
    python installer/build_plugin_installer.py C:\\builds\\TiaGitAddIn.addin
    python installer/build_plugin_installer.py --output D:\\plugin_installer.exe
"""

from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path
from typing import List, Optional


MAGIC = b"TGADDIN1"


class BuildError(Exception):
    """A problem the user can fix by changing the command or the plugin file."""


def main(argv: Optional[List[str]] = None) -> int:
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


def build(addin: Optional[str], output: Optional[str], portal_version: int) -> None:
    installer_dir = Path(__file__).resolve().parent
    stub_path = installer_dir / "plugin_installer_stub.exe"
    if not stub_path.is_file():
        raise BuildError(f"Installer stub is missing: {stub_path}")

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


def resolve_addin_path(repo_root: Path, requested: Optional[str]) -> Path:
    if requested:
        path = Path(requested).expanduser()
        if not path.is_file():
            raise BuildError(f"Add-in file not found: {requested}")
        return path.resolve()

    bin_root = repo_root / "src" / "TiaGitAddIn" / "bin"
    found = []
    if bin_root.is_dir():
        found = [item for item in bin_root.rglob("*.addin") if item.is_file()]
        found.sort(key=lambda item: item.stat().st_mtime, reverse=True)
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


def format_byte_count(size: int) -> str:
    if size < 1024:
        return f"{size} B"
    if size < 1024 * 1024:
        return f"{size / 1024:.1f} KB"
    return f"{size / 1024 / 1024:.2f} MB"


if __name__ == "__main__":
    sys.exit(main())
