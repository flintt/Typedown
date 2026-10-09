from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
WINUI_PROJECT = REPOSITORY_ROOT / "Dev" / "Typedown.WinUI" / "Typedown.WinUI.csproj"
LEGACY_PROJECT = REPOSITORY_ROOT / "Dev" / "Typedown" / "Typedown.csproj"
PACKAGING_PROJECT = (
    REPOSITORY_ROOT
    / "Tools"
    / "Typedown.Package"
    / "Typedown.Package.wapproj"
)


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def property_values(root: ET.Element, property_name: str) -> list[str]:
    return [
        (element.text or "").strip()
        for element in root.iter()
        if local_name(element.tag) == property_name
    ]


class WinUIMigrationArchitectureTests(unittest.TestCase):
    def setUp(self) -> None:
        if self._testMethodName in {
            "test_side_by_side_winui_project_exists",
            "test_keeps_legacy_application_and_packaging_projects",
        }:
            return
        if not WINUI_PROJECT.is_file():
            self.skipTest(
                "WinUI project properties cannot be checked until the side-by-side "
                "project exists"
            )
        self.project = ET.parse(WINUI_PROJECT).getroot()

    def test_side_by_side_winui_project_exists(self) -> None:
        self.assertTrue(
            WINUI_PROJECT.is_file(),
            "The side-by-side WinUI project is missing: "
            f"{WINUI_PROJECT.relative_to(REPOSITORY_ROOT)}",
        )

    def assert_single_property(self, name: str, expected: str) -> None:
        values = property_values(self.project, name)
        self.assertTrue(values, f"{name} must be declared explicitly")
        self.assertEqual(
            {value.casefold() for value in values},
            {expected.casefold()},
            f"Every {name} declaration must be {expected!r}; found {values!r}",
        )

    def test_targets_dotnet_10_for_windows(self) -> None:
        frameworks = property_values(self.project, "TargetFramework")
        self.assertTrue(frameworks, "TargetFramework must be declared")
        self.assertTrue(
            all(value.casefold().startswith("net10.0-windows") for value in frameworks),
            f"TargetFramework must target .NET 10 for Windows; found {frameworks!r}",
        )

    def test_uses_winui_3(self) -> None:
        self.assert_single_property("UseWinUI", "true")

    def test_pins_windows_app_sdk_2_5_1(self) -> None:
        versions = []
        for element in self.project.iter():
            if local_name(element.tag) != "PackageReference":
                continue
            package = element.attrib.get("Include") or element.attrib.get("Update")
            if package != "Microsoft.WindowsAppSDK":
                continue
            version = element.attrib.get("Version")
            if version is None:
                version_nodes = [
                    child
                    for child in element
                    if local_name(child.tag) == "Version"
                ]
                version = (version_nodes[0].text or "").strip() if version_nodes else ""
            versions.append(version)

        self.assertEqual(
            versions,
            ["2.5.1"],
            "The WinUI project must directly pin Microsoft.WindowsAppSDK 2.5.1",
        )

    def test_disables_aot_and_trimming(self) -> None:
        self.assert_single_property("PublishAot", "false")
        self.assert_single_property("PublishTrimmed", "false")

    def test_keeps_all_existing_windows_architectures(self) -> None:
        declarations = property_values(self.project, "Platforms")
        self.assertTrue(declarations, "Platforms must be declared")
        platforms = {
            item.strip().casefold()
            for declaration in declarations
            for item in declaration.split(";")
            if item.strip()
        }
        self.assertTrue(
            {"x64", "x86", "arm64"}.issubset(platforms),
            f"Platforms must retain x64, x86 and ARM64; found {sorted(platforms)!r}",
        )

    def test_keeps_legacy_application_and_packaging_projects(self) -> None:
        with self.subTest(project="legacy application"):
            self.assertTrue(
                LEGACY_PROJECT.is_file(),
                f"Legacy project was removed: {LEGACY_PROJECT.relative_to(REPOSITORY_ROOT)}",
            )
        with self.subTest(project="WAP packaging"):
            self.assertTrue(
                PACKAGING_PROJECT.is_file(),
                "Legacy WAP packaging project was removed: "
                f"{PACKAGING_PROJECT.relative_to(REPOSITORY_ROOT)}",
            )


if __name__ == "__main__":
    unittest.main()
