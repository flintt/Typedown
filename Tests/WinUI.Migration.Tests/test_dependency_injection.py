import re
import unittest
from pathlib import Path
from xml.etree import ElementTree


REPO_ROOT = Path(__file__).resolve().parents[2]
WINUI_ROOT = REPO_ROOT / "Dev" / "Typedown.WinUI"
PROJECT_FILE = WINUI_ROOT / "Typedown.WinUI.csproj"
COMPOSITION_FILE = WINUI_ROOT / "Services" / "ServiceConfiguration.cs"
SESSION_FILE = WINUI_ROOT / "Windowing" / "WindowSession.cs"
MANAGER_FILE = WINUI_ROOT / "Windowing" / "WindowManager.cs"


class DependencyInjectionTests(unittest.TestCase):
    def test_winui_host_references_the_net10_di_container(self) -> None:
        root = ElementTree.parse(PROJECT_FILE).getroot()
        packages = {
            item.attrib.get("Include"): item.attrib.get("Version")
            for item in root.findall(".//PackageReference")
        }
        self.assertEqual(packages.get("Microsoft.Extensions.DependencyInjection"), "10.0.0")

    def test_process_composition_root_registers_platform_lifetimes(self) -> None:
        self.assertTrue(COMPOSITION_FILE.exists(), "Missing WinUI process composition root")
        source = COMPOSITION_FILE.read_text(encoding="utf-8")
        self.assertIn("ServiceCollection", source)
        self.assertIn("ValidateScopes = true", source)
        self.assertRegex(source, r"AddSingleton\s*<\s*IAppDataPathProvider")
        self.assertRegex(source, r"AddSingleton\s*<\s*WindowManager")
        for contract in ("IWindowContext", "IUiDispatcher", "IDialogService", "IFilePickerService"):
            with self.subTest(contract=contract):
                self.assertRegex(source, rf"AddScoped\s*<\s*{contract}\s*>")

    def test_every_window_session_owns_and_disposes_one_scope(self) -> None:
        source = SESSION_FILE.read_text(encoding="utf-8")
        self.assertIn("IServiceScope", source)
        self.assertRegex(source, r"IServiceProvider\s+Services\s*\{")
        self.assertRegex(source, r"\w+\.Dispose\s*\(\s*\)")

    def test_window_manager_creates_scope_before_window_content(self) -> None:
        source = MANAGER_FILE.read_text(encoding="utf-8")
        self.assertIn("IServiceScopeFactory", source)
        scope_creation = source.find("CreateScope(")
        content_resolution = source.find("GetRequiredService<RootPage>")
        self.assertGreaterEqual(scope_creation, 0)
        self.assertGreater(content_resolution, scope_creation)
        self.assertNotIn("new RootPage", source)

    def test_window_specific_adapters_are_resolved_from_the_scope(self) -> None:
        source = COMPOSITION_FILE.read_text(encoding="utf-8")
        self.assertRegex(source, r"AddScoped\s*<\s*IDialogService\s*>\s*\(")
        self.assertRegex(source, r"AddScoped\s*<\s*IFilePickerService\s*>\s*\(")
        self.assertIn("WindowRegistration", source)


if __name__ == "__main__":
    unittest.main()
