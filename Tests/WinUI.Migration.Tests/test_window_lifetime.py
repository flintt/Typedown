import re
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WINDOWING_ROOT = REPO_ROOT / "Dev" / "Typedown.WinUI" / "Windowing"
APP_FILE = REPO_ROOT / "Dev" / "Typedown.WinUI" / "App.xaml.cs"
REQUIRED_FILES = (
    "WinUiDispatcher.cs",
    "WindowContext.cs",
    "WindowSession.cs",
    "WindowManager.cs",
)


def read_windowing_sources() -> str:
    return "\n".join(
        path.read_text(encoding="utf-8")
        for path in sorted(WINDOWING_ROOT.glob("*.cs"))
    )


class WindowLifetimeTests(unittest.TestCase):
    def setUp(self) -> None:
        if (
            self._testMethodName != "test_windowing_files_exist"
            and not all((WINDOWING_ROOT / name).exists() for name in REQUIRED_FILES)
        ):
            self.skipTest("WinUI window lifetime implementation is incomplete")

    def test_windowing_files_exist(self) -> None:
        missing = [
            name for name in REQUIRED_FILES if not (WINDOWING_ROOT / name).exists()
        ]
        self.assertFalse(missing, f"Missing window lifetime files: {missing}")

    def test_dispatcher_implements_contract_and_honors_cancellation(self) -> None:
        source = (WINDOWING_ROOT / "WinUiDispatcher.cs").read_text(encoding="utf-8")
        self.assertRegex(source, r"class\s+WinUiDispatcher\s*:\s*IUiDispatcher")
        self.assertIn("DispatcherQueue", source)
        self.assertIn("DispatcherQueuePriority", source)
        self.assertIn("CancellationToken", source)
        self.assertIn("TryEnqueue", source)
        self.assertRegex(source, r"IsShutdownStarted\s*(?:=>|\{)")

    def test_context_has_stable_id_and_window_operations(self) -> None:
        source = (WINDOWING_ROOT / "WindowContext.cs").read_text(encoding="utf-8")
        self.assertRegex(source, r"class\s+WindowContext\s*:\s*IWindowContext")
        self.assertRegex(source, r"WindowId\s+Id\s*\{\s*get\s*;\s*\}")
        self.assertIn("ActivateAsync", source)
        self.assertIn("RequestCloseAsync", source)
        self.assertIn("WindowActivationChangedEventArgs", source)

    def test_each_session_owns_its_lifetime_and_dispatcher(self) -> None:
        source = (WINDOWING_ROOT / "WindowSession.cs").read_text(encoding="utf-8")
        self.assertRegex(source, r"class\s+WindowSession\b")
        self.assertRegex(
            source,
            r"CancellationTokenSource\s+\w+\s*=\s*(?:new\s+CancellationTokenSource|new)\s*\(",
        )
        self.assertRegex(source, r"WinUiDispatcher\s+Dispatcher\s*\{\s*get\s*;\s*\}")
        self.assertRegex(source, r"WindowContext\s+Context\s*\{\s*get\s*;\s*\}")
        self.assertIn("LifetimeToken", source)
        self.assertRegex(source, r"\.Closed\s*\+=")
        self.assertIn("Cancel()", source)

    def test_manager_strongly_tracks_sessions_until_closed(self) -> None:
        source = (WINDOWING_ROOT / "WindowManager.cs").read_text(encoding="utf-8")
        self.assertRegex(
            source,
            r"Dictionary\s*<\s*WindowId\s*,\s*WindowSession\s*>",
        )
        self.assertRegex(source, r"\w+\.Add\s*\(\s*id\s*,\s*session\s*\)")
        self.assertRegex(source, r"\w+\.Remove\s*\(")
        self.assertRegex(source, r"new\s+WindowSession\s*\(")
        self.assertRegex(source, r"new\s+Window\s*\(")
        self.assertRegex(source, r"WindowSession\s+CreateWindow\s*\(")
        self.assertNotIn("Task.Run", source)

    def test_app_creates_first_window_through_manager_in_process(self) -> None:
        app_source = APP_FILE.read_text(encoding="utf-8")
        all_source = app_source + "\n" + read_windowing_sources()
        self.assertRegex(app_source, r"WindowManager\??\s+\w+")
        self.assertRegex(app_source, r"\.CreateWindow\s*\(")
        self.assertNotRegex(app_source, r"new\s+Window\s*[\({]")
        self.assertNotIn("Process.Start", all_source)


if __name__ == "__main__":
    unittest.main()
