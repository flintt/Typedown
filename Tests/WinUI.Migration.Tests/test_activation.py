import unittest
from pathlib import Path
from xml.etree import ElementTree


REPO_ROOT = Path(__file__).resolve().parents[2]
WINUI_ROOT = REPO_ROOT / "Dev" / "Typedown.WinUI"
ACTIVATION_ROOT = WINUI_ROOT / "Activation"
PROGRAM_FILE = WINUI_ROOT / "Program.cs"
APP_FILE = WINUI_ROOT / "App.xaml.cs"
PROJECT_FILE = WINUI_ROOT / "Typedown.WinUI.csproj"
PACKAGE_MANIFEST = WINUI_ROOT / "Package.appxmanifest"
REQUIRED_FILES = (
    "AppActivationBroker.cs",
    "AppActivationNormalizer.cs",
    "WinUiAppActivationService.cs",
)


def activation_sources() -> str:
    return "\n".join(
        path.read_text(encoding="utf-8")
        for path in sorted(ACTIVATION_ROOT.glob("*.cs"))
    )


class AppActivationTests(unittest.TestCase):
    def setUp(self) -> None:
        if (
            self._testMethodName != "test_activation_files_exist"
            and not all((ACTIVATION_ROOT / name).exists() for name in REQUIRED_FILES)
        ):
            self.skipTest("M3 activation implementation is incomplete")

    def test_activation_files_exist(self) -> None:
        missing = [
            name for name in REQUIRED_FILES if not (ACTIVATION_ROOT / name).exists()
        ]
        self.assertFalse(missing, f"Missing activation files: {missing}")

    def test_program_registers_before_starting_xaml_and_redirects_secondaries(self) -> None:
        source = PROGRAM_FILE.read_text(encoding="utf-8")
        expected_order = (
            "AppInstance.GetCurrent()",
            "GetActivatedEventArgs()",
            "FindOrRegisterForKey",
            "IsCurrent",
            "RedirectSecondaryActivation(mainInstance, initialActivation)",
            "Application.Start",
        )
        positions = [source.find(item) for item in expected_order]
        self.assertTrue(all(position >= 0 for position in positions), positions)
        self.assertEqual(positions, sorted(positions))
        self.assertIn("Microsoft.Windows.AppLifecycle", source)
        self.assertIn("RedirectActivationToAsync", source)
        self.assertNotIn("Process.Start", source)
        self.assertNotIn("HasNewWindowBypass", source)
        self.assertIn("typeof(Program).Assembly.GetName().Name", source)
        self.assertNotIn('"Typedown.Main"', source)

    def test_secondary_redirect_has_bounded_timeout_and_cancellation(self) -> None:
        source = PROGRAM_FILE.read_text(encoding="utf-8")
        self.assertIn("RedirectActivationTimeout", source)
        self.assertIn("RedirectCancellationTimeout", source)
        self.assertIn("CancellationTokenSource", source)
        self.assertRegex(source, r"AsTask\s*\(\s*\w+\.Token\s*\)")
        self.assertRegex(source, r"\.Wait\s*\(\s*RedirectActivationTimeout\s*\)")
        self.assertRegex(source, r"\.Cancel\s*\(")
        self.assertRegex(source, r"\.Wait\s*\(\s*RedirectCancellationTimeout\s*\)")
        self.assertNotRegex(source, r"\.Wait\s*\(\s*\)")

    def test_broker_buffers_redirects_without_running_ui_work_in_callback(self) -> None:
        source = (ACTIVATION_ROOT / "AppActivationBroker.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("AppInstance", source)
        self.assertRegex(source, r"\.Activated\s*\+=")
        self.assertIn("Channel.CreateUnbounded", source)
        self.assertIn("TryWrite", source)
        self.assertIn("ReadAllAsync", source)
        for blocking_call in (".Wait(", ".Result", "DispatcherQueue", "WindowManager"):
            with self.subTest(blocking_call=blocking_call):
                self.assertNotIn(blocking_call, source)

    def test_normalizer_covers_launch_file_command_line_and_protocol(self) -> None:
        source = (ACTIVATION_ROOT / "AppActivationNormalizer.cs").read_text(
            encoding="utf-8"
        )
        for activation_type in (
            "ILaunchActivatedEventArgs",
            "IFileActivatedEventArgs",
            "ICommandLineActivatedEventArgs",
            "IProtocolActivatedEventArgs",
        ):
            with self.subTest(activation_type=activation_type):
                self.assertIn(activation_type, source)
        for kind in ("Launch", "Files", "CommandLine", "Protocol"):
            with self.subTest(kind=kind):
                self.assertIn(f"AppActivationKind.{kind}", source)
        self.assertIn("FilePaths", source)
        self.assertIn("CommandLineArguments", source)
        self.assertIn("ProtocolUri", source)
        self.assertIn("OpenInNewWindow", source)

    def test_activation_service_implements_contract_and_uses_same_process_windows(self) -> None:
        source = (ACTIVATION_ROOT / "WinUiAppActivationService.cs").read_text(
            encoding="utf-8"
        )
        self.assertRegex(
            source,
            r"class\s+WinUiAppActivationService\s*:\s*IAppActivationService",
        )
        self.assertIn("ListenAsync", source)
        self.assertIn("ActivateAsync", source)
        self.assertIn("request.OpenInNewWindow", source)
        self.assertIn("windowManager.CreateWindow()", source)
        self.assertIn("DispatcherQueue", source)
        self.assertNotIn("Process.Start", source)

    def test_app_runs_nonblocking_activation_pump(self) -> None:
        source = APP_FILE.read_text(encoding="utf-8")
        self.assertIn("WinUiAppActivationService", source)
        self.assertIn("ListenAsync", source)
        self.assertIn("ActivateAsync", source)
        self.assertIn("CancellationTokenSource", source)
        self.assertNotIn("Process.Start", source)
        self.assertNotRegex(source, r"\.Wait\s*\(")
        self.assertNotIn(".Result", source)
        self.assertIn("ProcessInitialActivationAsync", source)
        self.assertRegex(
            source,
            r"await\s+service\.ActivateAsync\(request,\s*cancellationToken\)",
        )
        self.assertIn("Redirected application activation failed", source)

    def test_package_restores_existing_markdown_file_associations(self) -> None:
        manifest = ElementTree.parse(PACKAGE_MANIFEST).getroot()
        namespace = {"uap": "http://schemas.microsoft.com/appx/manifest/uap/windows10"}
        extensions = manifest.findall(".//uap:Extension", namespace)
        associations = [
            item
            for item in extensions
            if item.attrib.get("Category") == "windows.fileTypeAssociation"
        ]
        self.assertEqual(len(associations), 1)
        file_types = {
            item.text
            for item in associations[0].findall(".//uap:FileType", namespace)
        }
        self.assertTrue({".md", ".markdown"}.issubset(file_types))

    def test_activation_host_keeps_only_x64_and_arm64(self) -> None:
        root = ElementTree.parse(PROJECT_FILE).getroot()
        self.assertEqual(root.findtext(".//Platforms"), "x64;ARM64")
        self.assertEqual(root.findtext(".//RuntimeIdentifiers"), "win-x64;win-arm64")
        project_text = PROJECT_FILE.read_text(encoding="utf-8")
        self.assertNotRegex(project_text, r"\bx86\b")


if __name__ == "__main__":
    unittest.main()
