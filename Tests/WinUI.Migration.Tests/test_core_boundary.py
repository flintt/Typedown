from pathlib import Path
import glob
import unittest
import xml.etree.ElementTree as ET


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
PROJECT_DIRECTORY = REPOSITORY_ROOT / "Dev" / "Typedown.Core.Net10"
PROJECT_FILE = PROJECT_DIRECTORY / "Typedown.Core.Net10.csproj"

FORBIDDEN_SOURCE_REFERENCES = (
    "Windows.",
    "Microsoft.UI",
    "Microsoft.Web.WebView2",
    "Typedown.XamlUI",
)


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def property_values(root: ET.Element, property_name: str) -> list[str]:
    return [
        (element.text or "").strip()
        for element in root.iter()
        if local_name(element.tag) == property_name
    ]


def compile_sources(project: ET.Element) -> list[Path]:
    sources = {
        path.resolve()
        for path in PROJECT_DIRECTORY.rglob("*.cs")
        if "bin" not in path.parts and "obj" not in path.parts
    }
    for element in project.iter():
        if local_name(element.tag) != "Compile":
            continue
        include = element.attrib.get("Include")
        if not include or "$(" in include:
            continue
        pattern = str(PROJECT_DIRECTORY / include.replace("\\", "/"))
        for match in glob.glob(pattern, recursive=True):
            path = Path(match)
            if path.is_file():
                sources.add(path.resolve())
    return sorted(sources)


class CoreBoundaryTests(unittest.TestCase):
    def setUp(self) -> None:
        if self._testMethodName == "test_parallel_core_project_exists":
            return
        if not PROJECT_FILE.is_file():
            self.skipTest("Core boundary cannot be checked until its project exists")
        self.project = ET.parse(PROJECT_FILE).getroot()

    def test_parallel_core_project_exists(self) -> None:
        self.assertTrue(
            PROJECT_FILE.is_file(),
            "The side-by-side .NET 10 Core project is missing: "
            f"{PROJECT_FILE.relative_to(REPOSITORY_ROOT)}",
        )

    def test_is_plain_net10_sdk_project(self) -> None:
        self.assertEqual(self.project.attrib.get("Sdk"), "Microsoft.NET.Sdk")
        frameworks = property_values(self.project, "TargetFramework")
        self.assertEqual(
            frameworks,
            ["net10.0"],
            f"Core must target plain net10.0, without a Windows TFM; found {frameworks!r}",
        )

    def test_does_not_enable_a_windows_ui_stack(self) -> None:
        forbidden_packages = {
            "microsoft.windowsappsdk",
            "microsoft.web.webview2",
            "typedown.xamlui",
            "microsoft.netcore.universalwindowsplatform",
        }
        packages = {
            (element.attrib.get("Include") or element.attrib.get("Update") or "").casefold()
            for element in self.project.iter()
            if local_name(element.tag) == "PackageReference"
        }
        self.assertFalse(
            packages & forbidden_packages,
            f"Core references a Windows UI package: {sorted(packages & forbidden_packages)!r}",
        )
        self.assertNotIn(
            "true",
            {value.casefold() for value in property_values(self.project, "UseWinUI")},
            "Core must not enable WinUI",
        )

    def test_does_not_reference_the_legacy_or_winui_projects(self) -> None:
        references = [
            (element.attrib.get("Include") or "").replace("\\", "/").casefold()
            for element in self.project.iter()
            if local_name(element.tag) == "ProjectReference"
        ]
        forbidden = [
            reference
            for reference in references
            if reference.endswith("/typedown.core/typedown.core.csproj")
            or reference.endswith("/typedown.winui/typedown.winui.csproj")
            or reference.endswith("/typedown/typedown.csproj")
        ]
        self.assertFalse(
            forbidden,
            f"Core must not depend on a legacy or UI project: {forbidden!r}",
        )

    def test_fody_configuration_is_local_to_the_parallel_project(self) -> None:
        packages = {
            (element.attrib.get("Include") or "").casefold()
            for element in self.project.iter()
            if local_name(element.tag) == "PackageReference"
        }
        if "propertychanged.fody" not in packages:
            self.skipTest("The current Core slice does not use PropertyChanged.Fody")

        configuration = PROJECT_DIRECTORY / "FodyWeavers.xml"
        schema = PROJECT_DIRECTORY / "FodyWeavers.xsd"
        self.assertTrue(
            configuration.is_file(),
            "Fody only loads FodyWeavers.xml from the project directory; a cross-directory property leaves a warning and generates a file during build",
        )
        self.assertTrue(
            schema.is_file(),
            "Fody otherwise generates an untracked schema during the Windows build",
        )
        self.assertIn(
            "FodyWeavers.xsd",
            configuration.read_text(encoding="utf-8-sig"),
        )

    def test_linked_core_sources_have_no_ui_or_picker_dependencies(self) -> None:
        sources = compile_sources(self.project)
        self.assertGreaterEqual(
            len(sources),
            39,
            "The Core slice must cover at least 39 existing platform-neutral source files",
        )
        violations = []
        for source in sources:
            text = source.read_text(encoding="utf-8-sig")
            for forbidden in FORBIDDEN_SOURCE_REFERENCES:
                if forbidden in text:
                    violations.append(
                        f"{source.relative_to(REPOSITORY_ROOT)}: {forbidden}"
                    )
        self.assertFalse(
            violations,
            "Platform UI references crossed the Core boundary:\n" + "\n".join(violations),
        )


if __name__ == "__main__":
    unittest.main()
