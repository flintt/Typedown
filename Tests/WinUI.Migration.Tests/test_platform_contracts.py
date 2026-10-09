import re
import unittest
from pathlib import Path
from xml.etree import ElementTree


REPO_ROOT = Path(__file__).resolve().parents[2]
CONTRACTS_ROOT = REPO_ROOT / "Dev" / "Typedown.Contracts"
PROJECT_FILE = CONTRACTS_ROOT / "Typedown.Contracts.csproj"


def read_contract_sources() -> str:
    return "\n".join(
        path.read_text(encoding="utf-8")
        for path in sorted(CONTRACTS_ROOT.rglob("*.cs"))
    )


class PlatformContractTests(unittest.TestCase):
    def setUp(self) -> None:
        if (
            self._testMethodName != "test_contracts_project_exists"
            and not PROJECT_FILE.exists()
        ):
            self.skipTest("Typedown.Contracts has not been created yet")

    def test_contracts_project_exists(self) -> None:
        self.assertTrue(
            PROJECT_FILE.exists(),
            "M2 requires Dev/Typedown.Contracts/Typedown.Contracts.csproj",
        )

    def test_contracts_project_is_plain_net10(self) -> None:
        root = ElementTree.parse(PROJECT_FILE).getroot()
        target_framework = root.findtext(".//TargetFramework")
        explicit_dependencies = (
            root.findall(".//PackageReference")
            + root.findall(".//ProjectReference")
            + root.findall(".//FrameworkReference")
        )

        self.assertEqual(root.attrib.get("Sdk"), "Microsoft.NET.Sdk")
        self.assertEqual(target_framework, "net10.0")
        self.assertFalse(explicit_dependencies, "Contracts must only depend on the BCL")
        self.assertIsNone(root.find(".//UseWinUI"))
        self.assertIsNone(root.find(".//UseWPF"))

    def test_contracts_do_not_expose_platform_ui_types(self) -> None:
        source = read_contract_sources()
        forbidden = {
            "Microsoft.UI": "WinUI",
            "Windows.UI": "Windows UI",
            "Windows.Storage": "WinRT storage",
            "Windows.Foundation": "WinRT async",
            "Microsoft.Web.WebView2": "WebView2",
            "CoreWebView2": "WebView2",
            "CoreDispatcher": "WinRT dispatcher",
            "DispatcherQueue": "WinUI dispatcher",
            "StorageFile": "WinRT storage",
            "StorageFolder": "WinRT storage",
            "AppWindow": "Windows App SDK window",
            "WindowNative": "native window interop",
            "XamlRoot": "XAML",
            "FrameworkElement": "XAML",
            "IntPtr": "native handle",
            "nint": "native handle",
        }

        for token, category in forbidden.items():
            with self.subTest(token=token):
                self.assertNotIn(
                    token,
                    source,
                    f"Contracts leak a {category} type through {token}",
                )

    def test_required_interfaces_and_cancellation_are_present(self) -> None:
        source = read_contract_sources()
        for interface_name in (
            "IAppDataPathProvider",
            "IUiDispatcher",
            "IWindowContext",
            "IDialogService",
            "IFilePickerService",
            "IAppActivationService",
        ):
            with self.subTest(interface=interface_name):
                self.assertRegex(source, rf"public\s+interface\s+{interface_name}\b")

        async_members = re.findall(
            r"(?:Task|ValueTask|IAsyncEnumerable)<*[^;{]*[;{]", source
        )
        self.assertTrue(async_members, "Contracts should expose asynchronous boundaries")
        for member in async_members:
            with self.subTest(member=member):
                self.assertIn("CancellationToken", member)

    def test_window_context_uses_a_stable_platform_neutral_identity(self) -> None:
        source = read_contract_sources()
        self.assertRegex(source, r"public\s+readonly\s+record\s+struct\s+WindowId\b")
        self.assertRegex(
            source,
            r"public\s+interface\s+IWindowContext\b[\s\S]*?WindowId\s+Id\s*\{\s*get\s*;\s*\}",
        )

    def test_dialog_contract_has_explicit_result_and_cancellation(self) -> None:
        source = read_contract_sources()
        self.assertRegex(source, r"public\s+enum\s+DialogOutcome\b")
        self.assertRegex(source, r"\bCancelled\b")
        self.assertRegex(
            source,
            r"ShowAsync\s*\(\s*DialogRequest\s+request\s*,\s*CancellationToken\s+cancellationToken",
        )

    def test_file_picker_supports_explicit_cancellation_and_multiple_files(self) -> None:
        source = read_contract_sources()
        self.assertRegex(source, r"bool\s+AllowMultiple\b")
        self.assertRegex(source, r"IReadOnlyList<string>\s+Paths\b")
        self.assertRegex(source, r"FilePickerStatus\s+Status\b")
        self.assertRegex(
            source,
            r"OpenFilesAsync\s*\(\s*OpenFilePickerRequest\s+request\s*,\s*CancellationToken\s+cancellationToken",
        )

    def test_activation_carries_files_arguments_and_target_window_result(self) -> None:
        source = read_contract_sources()
        self.assertRegex(source, r"IReadOnlyList<string>\s+FilePaths\b")
        self.assertRegex(source, r"IReadOnlyList<string>\s+CommandLineArguments\b")
        self.assertRegex(source, r"WindowId\?\s+WindowId\b")
        self.assertRegex(
            source,
            r"ActivateAsync\s*\(\s*AppActivationRequest\s+request\s*,\s*CancellationToken\s+cancellationToken",
        )
        self.assertRegex(
            source,
            r"ListenAsync\s*\(\s*CancellationToken\s+cancellationToken",
        )


if __name__ == "__main__":
    unittest.main()
