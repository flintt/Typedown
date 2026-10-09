import re
import unittest
from pathlib import Path
from xml.etree import ElementTree


REPO_ROOT = Path(__file__).resolve().parents[2]
WINUI_ROOT = REPO_ROOT / "Dev" / "Typedown.WinUI"
PROJECT_FILE = WINUI_ROOT / "Typedown.WinUI.csproj"
APP_FILE = WINUI_ROOT / "App.xaml.cs"
COMPOSITION_FILE = WINUI_ROOT / "Services" / "ServiceConfiguration.cs"
SERVICE_FILE = WINUI_ROOT / "Automation" / "WinUIAutomationService.cs"
MANAGER_FILE = WINUI_ROOT / "Windowing" / "WindowManager.cs"


class AutomationStatusTests(unittest.TestCase):
    def test_winui_host_carries_the_existing_protocol_and_secure_listener(self) -> None:
        root = ElementTree.parse(PROJECT_FILE).getroot()
        references = {
            item.attrib.get("Include") for item in root.findall(".//ProjectReference")
        }
        linked_sources = {
            item.attrib.get("Include") for item in root.findall(".//Compile")
        }

        self.assertIn(r"..\Typedown.Automation\Typedown.Automation.csproj", references)
        self.assertIn(
            r"..\Typedown\Services\Automation\SecurePipeListener.cs",
            linked_sources,
        )

    def test_process_composition_starts_one_automation_service(self) -> None:
        app = APP_FILE.read_text(encoding="utf-8")
        composition = COMPOSITION_FILE.read_text(encoding="utf-8")

        self.assertRegex(
            composition,
            r"AddSingleton\s*<\s*WinUIAutomationService\s*>",
        )
        self.assertIn("GetRequiredService<WinUIAutomationService>", app)

    def test_service_preserves_the_opt_in_switch_and_only_advertises_status(self) -> None:
        source = SERVICE_FILE.read_text(encoding="utf-8")

        self.assertIn('SettingName = "AllowLocalAutomation"', source)
        self.assertIn("settings.Changed +=", source)
        self.assertRegex(source, r"settings\.Get\s*\(\s*SettingName\s*,\s*false\s*\)")
        self.assertIn("SecurePipeListener.CurrentUserSid()", source)
        self.assertIn("AutomationEndpoint.PipeName", source)
        self.assertRegex(
            source,
            r'new\s+MethodDescriptor\s*\(\s*"app\.getState"',
        )
        self.assertNotIn("DocumentMethods.AddTo", source)
        self.assertNotIn("SettingsMethods.AddTo", source)
        self.assertNotIn("ViewMethods.AddTo", source)

    def test_status_reads_a_thread_safe_window_snapshot(self) -> None:
        service = SERVICE_FILE.read_text(encoding="utf-8")
        manager = MANAGER_FILE.read_text(encoding="utf-8")

        self.assertIn("GetAutomationSnapshot", service)
        self.assertRegex(manager, r"WindowAutomationSnapshot\s+GetAutomationSnapshot")
        self.assertRegex(manager, r"lock\s*\(\s*sessionsGate\s*\)")
        self.assertRegex(manager, r"\.Context\)?[\s\S]*?\.IsActive")


if __name__ == "__main__":
    unittest.main()
