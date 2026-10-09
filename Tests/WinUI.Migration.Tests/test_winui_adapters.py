from pathlib import Path
import re
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
SERVICES = REPOSITORY_ROOT / "Dev" / "Typedown.WinUI" / "Services"
APP_DATA = SERVICES / "WinUIAppDataPathProvider.cs"
FILE_PICKER = SERVICES / "WinUIFilePickerService.cs"
DIALOG = SERVICES / "WinUIDialogService.cs"


class WinUIAdapterTests(unittest.TestCase):
    def setUp(self) -> None:
        for path in (APP_DATA, FILE_PICKER, DIALOG):
            self.assertTrue(
                path.is_file(),
                f"WinUI adapter is missing: {path.relative_to(REPOSITORY_ROOT)}",
            )
        self.app_data = APP_DATA.read_text(encoding="utf-8")
        self.file_picker = FILE_PICKER.read_text(encoding="utf-8")
        self.dialog = DIALOG.read_text(encoding="utf-8")

    def test_app_data_paths_preserve_the_existing_typedown_layout(self) -> None:
        self.assertIn("IAppDataPathProvider", self.app_data)
        self.assertIn("ApplicationData.Current.LocalFolder.Path", self.app_data)
        self.assertIn("Environment.SpecialFolder.MyDocuments", self.app_data)
        self.assertIn("Environment.SpecialFolder.LocalApplicationData", self.app_data)
        for name in ("Settings.json", "Storage.db", "Backup", "themes"):
            with self.subTest(name=name):
                self.assertIn(f'"{name}"', self.app_data)
        self.assertNotRegex(
            self.app_data,
            r"(?i)Path\.Combine\s*\([^;]*[\"'](?:Typedown\.)?WinUI[\"']",
        )

    def test_picker_is_owned_by_the_current_window(self) -> None:
        self.assertIn("IFilePickerService", self.file_picker)
        self.assertRegex(self.file_picker, r"\bWindow\s+window\b")
        self.assertIn("WindowNative.GetWindowHandle(window)", self.file_picker)
        self.assertGreaterEqual(
            self.file_picker.count("InitializeWithWindow.Initialize"),
            3,
        )

    def test_picker_supports_multiple_files_cancellation_and_dismissal(self) -> None:
        self.assertIn("request.AllowMultiple", self.file_picker)
        self.assertIn("PickMultipleFilesAsync", self.file_picker)
        self.assertIn("PickSingleFileAsync", self.file_picker)
        self.assertRegex(
            self.file_picker,
            r"(?s)PickMultipleFilesAsync\s*\(\s*\).*?AsTask\s*\(\s*cancellationToken\s*\)",
        )
        self.assertIn("FilePickerStatus.Cancelled", self.file_picker)
        self.assertIn("FilePickerStatus.Accepted", self.file_picker)

    def test_dialog_uses_the_current_window_xaml_root(self) -> None:
        self.assertIn("IDialogService", self.dialog)
        self.assertRegex(self.dialog, r"\bWindow\s+window\b")
        self.assertIn("window.Content", self.dialog)
        self.assertRegex(self.dialog, r"XamlRoot\s*=\s*ResolveXamlRoot\s*\(\s*\)")
        self.assertIn("AsTask(cancellationToken)", self.dialog)

    def test_dialog_maps_every_contract_outcome_explicitly(self) -> None:
        for outcome in (
            "DialogOutcome.Primary",
            "DialogOutcome.Secondary",
            "DialogOutcome.Cancelled",
            "DialogOutcome.Dismissed",
        ):
            with self.subTest(outcome=outcome):
                self.assertIn(outcome, self.dialog)
        self.assertIn("CloseButtonClick", self.dialog)
        self.assertRegex(
            self.dialog,
            r"DialogDefaultButton\.Cancel\s*=>\s*ContentDialogButton\.Close",
        )


if __name__ == "__main__":
    unittest.main()
