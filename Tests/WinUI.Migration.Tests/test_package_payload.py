from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
WINUI_PROJECT = REPOSITORY_ROOT / "Dev" / "Typedown.WinUI" / "Typedown.WinUI.csproj"
CLI_PROJECT = REPOSITORY_ROOT / "Tools" / "Typedown.Cli" / "Typedown.Cli.csproj"
MCP_PROJECT = REPOSITORY_ROOT / "Tools" / "Typedown.Mcp" / "Typedown.Mcp.csproj"
CLI_SOURCE = REPOSITORY_ROOT / "Tools" / "Typedown.Cli" / "Cli.cs"
UNIX_SOCKET_SOURCE = (
    REPOSITORY_ROOT / "Dev" / "Typedown.Automation" / "Unix" / "UnixSocketListener.cs"
)


def target_frameworks(path: Path) -> set[str]:
    root = ET.parse(path).getroot()
    value = root.findtext(".//TargetFrameworks") or root.findtext(".//TargetFramework") or ""
    return {item.strip() for item in value.split(";") if item.strip()}


class PackagePayloadTests(unittest.TestCase):
    def test_cli_and_mcp_have_a_net10_target_for_the_migrated_package(self) -> None:
        self.assertIn("net10.0", target_frameworks(CLI_PROJECT))
        self.assertIn("net10.0", target_frameworks(MCP_PROJECT))

    def test_winui_packaging_publishes_the_cli_for_the_current_runtime(self) -> None:
        root = ET.parse(WINUI_PROJECT).getroot()
        targets = {
            item.attrib.get("Name"): item
            for item in root.findall(".//Target")
        }
        target = targets.get("PublishAutomationCliForPackaging")
        self.assertIsNotNone(target, "The WinUI package does not produce its CLI payload")

        source = ET.tostring(target, encoding="unicode")
        restore = target.find("MSBuild[@Targets='Restore']")
        self.assertIsNotNone(restore)
        self.assertNotIn(
            "TargetFramework=",
            restore.attrib.get("Properties", ""),
            "A global TFM would overwrite the netstandard target of project references",
        )
        self.assertTrue(
            (root.findtext(".//AutomationCliProject") or "").endswith(
                "Tools\\Typedown.Cli\\Typedown.Cli.csproj"
            )
        )
        self.assertEqual(root.findtext(".//AutomationCliTargetFramework"), "net10.0")
        self.assertIn("BeforeTargets", target.attrib)
        self.assertIn("_ComputeAppxPackagePayload", target.attrib["BeforeTargets"])
        self.assertIn("Projects=\"$(AutomationCliProject)\"", source)
        self.assertIn("Targets=\"Restore\"", source)
        self.assertIn("Targets=\"Publish\"", source)
        self.assertIn("TargetFramework=$(AutomationCliTargetFramework)", source)
        self.assertIn("RuntimeIdentifier=$(RuntimeIdentifier)", source)
        self.assertIn("RuntimeIdentifiers=$(RuntimeIdentifier)", source)
        self.assertIn("SelfContained=true", source)
        self.assertIn("PackagingOutputs", source)
        self.assertIn("$(BrandName)\\", source)
        self.assertIn("%(_AutomationCliPackageFile.RecursiveDir)", source)
        self.assertIn("Exclude=", source)
        self.assertIn("*.pdb", source)

    def test_winui_packaging_restores_only_the_net10_cli_graph(self) -> None:
        winui_source = WINUI_PROJECT.read_text(encoding="utf-8")
        self.assertGreaterEqual(winui_source.count("TypedownWinUIPackaging=true"), 2)

        for project in (CLI_PROJECT, MCP_PROJECT):
            root = ET.parse(project).getroot()
            declarations = [
                (item.attrib.get("Condition", ""), item.text or "")
                for item in root.findall(".//TargetFrameworks")
            ]
            self.assertIn(("'$(TypedownWinUIPackaging)' == 'true'", "net10.0"), declarations)
            normal = [value for condition, value in declarations if "!= 'true'" in condition]
            self.assertEqual(normal, ["net10.0;net8.0;netcoreapp3.1"])

    def test_unix_transport_is_explicitly_guarded_from_windows(self) -> None:
        unix_source = UNIX_SOCKET_SOURCE.read_text(encoding="utf-8")
        cli_source = CLI_SOURCE.read_text(encoding="utf-8")
        self.assertIn('[UnsupportedOSPlatform("windows")]', unix_source)
        self.assertGreaterEqual(cli_source.count("OperatingSystem.IsWindows()"), 2)
        self.assertEqual(
            cli_source.count("RuntimeInformation.IsOSPlatform(OSPlatform.Windows)"),
            1,
            "The netcoreapp3.1 user-id path still needs its compatible platform check",
        )

    def test_packaging_target_cannot_select_x86(self) -> None:
        source = WINUI_PROJECT.read_text(encoding="utf-8").casefold()
        self.assertNotIn("win-x86", source)
        self.assertNotIn("platform=x86", source)


if __name__ == "__main__":
    unittest.main()
