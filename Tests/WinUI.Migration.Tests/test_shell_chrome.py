import unittest
from pathlib import Path
from xml.etree import ElementTree


REPO_ROOT = Path(__file__).resolve().parents[2]
ROOT_XAML = REPO_ROOT / "Dev" / "Typedown.WinUI" / "RootPage.xaml"
ROOT_CODE = REPO_ROOT / "Dev" / "Typedown.WinUI" / "RootPage.xaml.cs"
SESSION_CONTRACT = (
    REPO_ROOT
    / "Dev"
    / "Typedown.Contracts"
    / "Editor"
    / "IEditorDocumentSession.cs"
)
SESSION_IMPLEMENTATION = (
    REPO_ROOT
    / "Dev"
    / "Typedown.Core.Net10"
    / "Editor"
    / "EditorDocumentSession.cs"
)


class ShellChromeTests(unittest.TestCase):
    def test_document_session_exposes_one_consistent_observable_snapshot(self) -> None:
        contract = SESSION_CONTRACT.read_text(encoding="utf-8")
        implementation = SESSION_IMPLEMENTATION.read_text(encoding="utf-8")

        self.assertIn("EditorDocumentState", contract)
        self.assertIn("EditorDocumentState State", contract)
        self.assertIn("StateChanged", contract)
        self.assertIn('case "StateChange":', implementation)
        self.assertIn('payload["state"]?["wordCount"]', implementation)

    def test_root_contains_localized_file_menu_title_and_status_regions(self) -> None:
        ElementTree.parse(ROOT_XAML)
        xaml = ROOT_XAML.read_text(encoding="utf-8")

        for name in (
            "ShellMenuBar",
            "FileMenu",
            "OpenFileItem",
            "SaveFileItem",
            "DocumentTitleText",
            "StatusBar",
            "WordCountText",
            "CharacterCountText",
        ):
            with self.subTest(name=name):
                self.assertIn(f'x:Name="{name}"', xaml)

        self.assertIn('AutomationProperties.AutomationId="MenuBarFileItem"', xaml)
        self.assertIn('AutomationProperties.AutomationId="StatusBar"', xaml)

    def test_shell_uses_window_scoped_services_and_dispatches_state_changes(self) -> None:
        code = ROOT_CODE.read_text(encoding="utf-8")

        self.assertIn("IFilePickerService", code)
        self.assertIn("IWindowContext", code)
        self.assertIn("documentSession.StateChanged +=", code)
        self.assertIn("stringLocalizer.LanguageChanged +=", code)
        self.assertIn("windowContext.Dispatcher", code)
        self.assertIn("OpenFilesAsync", code)
        self.assertIn("documentSession.OpenAsync", code)
        self.assertIn("documentSession.SaveAsync", code)


if __name__ == "__main__":
    unittest.main()
