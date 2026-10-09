from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
WINUI_MANIFEST = REPOSITORY_ROOT / "Dev" / "Typedown.WinUI" / "Package.appxmanifest"
STABLE_MANIFEST = REPOSITORY_ROOT / "Tools" / "Typedown.Package" / "Package.appxmanifest"
WINUI_PROJECT = REPOSITORY_ROOT / "Dev" / "Typedown.WinUI" / "Typedown.WinUI.csproj"
BRANDING = REPOSITORY_ROOT / "Branding.props"

FOUNDATION = "http://schemas.microsoft.com/appx/manifest/foundation/windows10"
UAP = "http://schemas.microsoft.com/appx/manifest/uap/windows10"
UAP5 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
RESCAP = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
NS = {"f": FOUNDATION, "uap": UAP, "uap5": UAP5, "rescap": RESCAP}
EXPECTED_IDENTITY_NAME = "flintthuang.Typedown"
EXPECTED_PUBLISHER = "CN=1610AF00-8DF9-41AC-B61B-D854FDF5C66B"
EXPECTED_PUBLISHER_DISPLAY_NAME = "flintthuang"


def text_of(root: ET.Element, name: str) -> str:
    element = root.find(f".//{name}")
    if element is None or element.text is None:
        raise AssertionError(f"Missing {name}")
    return element.text.strip()


def platform_values(project: ET.Element, name: str) -> set[str]:
    values: set[str] = set()
    for element in project.findall(f".//{name}"):
        values.update(
            part.strip().casefold()
            for part in (element.text or "").split(";")
            if part.strip()
        )
    return values


class PackageManifestCompatibilityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.manifest = ET.parse(WINUI_MANIFEST).getroot()
        cls.stable = ET.parse(STABLE_MANIFEST).getroot()
        cls.project = ET.parse(WINUI_PROJECT).getroot()
        cls.branding = ET.parse(BRANDING).getroot()

    def test_winui_package_uses_the_fork_identity(self) -> None:
        current = self.manifest.find("f:Identity", NS)
        stable = self.stable.find("f:Identity", NS)
        self.assertIsNotNone(current)
        self.assertIsNotNone(stable)
        self.assertEqual(current.get("Name"), EXPECTED_IDENTITY_NAME)
        self.assertEqual(current.get("Publisher"), EXPECTED_PUBLISHER)
        self.assertEqual(current.get("Version"), stable.get("Version"))
        self.assertEqual(
            self.manifest.findtext(
                "f:Properties/f:PublisherDisplayName", namespaces=NS
            ),
            EXPECTED_PUBLISHER_DISPLAY_NAME,
        )
        self.assertNotIn(
            "ProcessorArchitecture",
            current.attrib,
            "The per-platform package build must stamp its own architecture",
        )

    def test_brand_and_main_executable_match_the_existing_product(self) -> None:
        brand_name = text_of(self.branding, "BrandName")
        exe_name = text_of(self.branding, "BrandExeName")
        self.assertEqual(
            self.manifest.findtext("f:Properties/f:DisplayName", namespaces=NS),
            brand_name,
        )
        application = self.manifest.find("f:Applications/f:Application[@Id='App']", NS)
        self.assertIsNotNone(application)
        self.assertEqual(application.get("Executable"), "$targetnametoken$.exe")
        self.assertEqual(application.get("EntryPoint"), "$targetentrypoint$")
        self.assertIn(
            "<AssemblyName>$(BrandExeName)</AssemblyName>",
            WINUI_PROJECT.read_text(encoding="utf-8"),
        )
        self.assertEqual(exe_name, "Typedown")

    def test_markdown_and_text_file_associations_match_the_stable_package(self) -> None:
        current = self.manifest.find(
            ".//uap:FileTypeAssociation[@Name='typedown']", NS
        )
        stable = self.stable.find(
            ".//uap:FileTypeAssociation[@Name='typedown']", NS
        )
        self.assertIsNotNone(current, "The WinUI package has no Typedown file association")
        self.assertIsNotNone(stable)

        def file_types(association: ET.Element) -> list[tuple[str, str | None]]:
            return [
                ((element.text or "").strip(), element.get("ContentType"))
                for element in association.findall(".//uap:FileType", NS)
            ]

        self.assertEqual(file_types(current), file_types(stable))
        self.assertEqual(
            current.findtext("uap:DisplayName", namespaces=NS),
            text_of(self.branding, "BrandName"),
        )
        self.assertEqual(
            current.findtext("uap:Logo", namespaces=NS),
            "Assets\\Markdown.png",
        )

    def test_cli_execution_alias_matches_branding_and_stable_layout(self) -> None:
        brand_name = text_of(self.branding, "BrandName")
        cli_name = text_of(self.branding, "BrandCliName")
        cli = self.manifest.find("f:Applications/f:Application[@Id='Cli']", NS)
        self.assertIsNotNone(cli, "The packaged automation CLI application is missing")
        expected_executable = f"{brand_name}\\{cli_name}.exe"
        self.assertEqual(cli.get("Executable"), expected_executable)
        self.assertEqual(cli.get("EntryPoint"), "Windows.FullTrustApplication")
        visual = cli.find("uap:VisualElements", NS)
        self.assertEqual(visual.get("AppListEntry"), "none")

        alias_extension = cli.find(
            "f:Extensions/uap5:Extension[@Category='windows.appExecutionAlias']", NS
        )
        self.assertIsNotNone(alias_extension)
        self.assertEqual(alias_extension.get("Executable"), expected_executable)
        self.assertEqual(
            alias_extension.get("EntryPoint"), "Windows.FullTrustApplication"
        )
        alias = alias_extension.find("uap5:AppExecutionAlias/uap5:ExecutionAlias", NS)
        self.assertIsNotNone(alias)
        self.assertEqual(alias.get("Alias"), f"{cli_name}.exe")
        self.assertIn("uap5", self.manifest.get("IgnorableNamespaces", "").split())

    def test_minimum_windows_version_matches_the_winui_project(self) -> None:
        families = self.manifest.findall("f:Dependencies/f:TargetDeviceFamily", NS)
        self.assertEqual(len(families), 1)
        self.assertEqual(families[0].get("Name"), "Windows.Desktop")
        self.assertEqual(
            families[0].get("MinVersion"),
            text_of(self.project, "TargetPlatformMinVersion"),
        )
        self.assertEqual(
            families[0].get("MaxVersionTested"),
            text_of(self.project, "TargetPlatformVersion"),
        )

    def test_release_architectures_are_x64_and_arm64_only(self) -> None:
        self.assertEqual(platform_values(self.project, "Platforms"), {"x64", "arm64"})
        self.assertEqual(
            platform_values(self.project, "RuntimeIdentifiers"),
            {"win-x64", "win-arm64"},
        )
        source = WINUI_MANIFEST.read_text(encoding="utf-8").casefold()
        self.assertNotIn("x86", source)

    def test_capabilities_preserve_network_and_full_trust_access(self) -> None:
        self.assertIsNotNone(
            self.manifest.find("f:Capabilities/f:Capability[@Name='internetClient']", NS)
        )
        self.assertIsNotNone(
            self.manifest.find(
                "f:Capabilities/rescap:Capability[@Name='runFullTrust']", NS
            )
        )


if __name__ == "__main__":
    unittest.main()
