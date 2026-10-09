from pathlib import Path
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
CONTRACT = (
    REPOSITORY_ROOT
    / "Dev"
    / "Typedown.Contracts"
    / "Editor"
    / "IMarkdownEditorBridge.cs"
)
HOST = (
    REPOSITORY_ROOT
    / "Dev"
    / "Typedown.WinUI"
    / "Editor"
    / "WinUIEditorHost.cs"
)
ENVIRONMENT = (
    REPOSITORY_ROOT
    / "Dev"
    / "Typedown.WinUI"
    / "Editor"
    / "WinUIWebViewEnvironmentService.cs"
)
ROOT_PAGE = REPOSITORY_ROOT / "Dev" / "Typedown.WinUI" / "RootPage.xaml.cs"
ROOT_XAML = REPOSITORY_ROOT / "Dev" / "Typedown.WinUI" / "RootPage.xaml"
SERVICES = (
    REPOSITORY_ROOT
    / "Dev"
    / "Typedown.WinUI"
    / "Services"
    / "ServiceConfiguration.cs"
)


class EditorBridgeTests(unittest.TestCase):
    def test_contract_is_raw_and_platform_neutral(self) -> None:
        source = CONTRACT.read_text(encoding="utf-8")
        self.assertIn("interface IMarkdownEditorBridge", source)
        self.assertIn("RawMessageReceived", source)
        self.assertIn("TryPostJson", source)
        self.assertIn("EditorBridgeState", source)
        for forbidden in ("Microsoft.UI", "Windows.UI", "WebView2", "Xaml", "JToken"):
            self.assertNotIn(forbidden, source)

    def test_winui_host_only_transports_the_existing_wire_json(self) -> None:
        source = HOST.read_text(encoding="utf-8")
        self.assertIn("IMarkdownEditorBridge", source)
        self.assertIn("new WebView2", source)
        self.assertIn("EnsureCoreWebView2Async", source)
        self.assertIn("TryGetWebMessageAsString", source)
        self.assertIn("RawMessageReceived?.Invoke", source)
        self.assertIn("PostWebMessageAsString(json)", source)
        self.assertNotIn("GetSettings", source)
        self.assertNotIn("JsonDocument", source)

    def test_host_loads_the_packaged_bundle_with_current_local_image_compatibility(self) -> None:
        host = HOST.read_text(encoding="utf-8")
        environment = ENVIRONMENT.read_text(encoding="utf-8")
        self.assertIn('"Resources", "Statics", "index.html"', host)
        self.assertIn("new Uri(indexPath).AbsoluteUri", host)
        self.assertIn("--disable-web-security", environment)
        self.assertIn("--allow-file-access-from-files", environment)
        self.assertIn('Path.Combine(pathProvider.CacheDirectory, "WebView2")', environment)

    def test_webview_failures_and_disposal_are_observable(self) -> None:
        source = HOST.read_text(encoding="utf-8")
        self.assertIn("NavigationCompleted", source)
        self.assertIn("ProcessFailed", source)
        self.assertIn("EditorBridgeState.Faulted", source)
        self.assertIn("EditorBridgeState.Disposed", source)
        self.assertIn("public void Dispose()", source)

    def test_each_window_scope_owns_one_editor_host(self) -> None:
        root_page = ROOT_PAGE.read_text(encoding="utf-8")
        root_xaml = ROOT_XAML.read_text(encoding="utf-8")
        services = SERVICES.read_text(encoding="utf-8")
        self.assertIn("WinUIEditorHost editorHost", root_page)
        self.assertIn("EditorPresenter.Content = editorHost", root_page)
        self.assertIn('x:Name="EditorPresenter"', root_xaml)
        self.assertIn("AddScoped<WinUIEditorHost>", services)
        self.assertIn("AddScoped<IMarkdownEditorBridge>", services)
        self.assertIn("AddSingleton<WinUIWebViewEnvironmentService>", services)


if __name__ == "__main__":
    unittest.main()
