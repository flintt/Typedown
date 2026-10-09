from pathlib import Path
import re
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
BUILD_SCRIPT = REPOSITORY_ROOT / "Tools" / "WinUI" / "build.ps1"


class WinUIBuildEntrypointTests(unittest.TestCase):
    def setUp(self) -> None:
        self.assertTrue(
            BUILD_SCRIPT.is_file(),
            "The repeatable WinUI build entrypoint is missing: Tools/WinUI/build.ps1",
        )
        self.source = BUILD_SCRIPT.read_text(encoding="utf-8")

    def test_defaults_to_debug_local_for_all_supported_platforms(self) -> None:
        self.assertRegex(
            self.source,
            r"(?is)\[string\]\s*\$Configuration\s*=\s*[\"']Debug_Local[\"']",
        )
        platform_default = re.search(
            r"(?is)\[string\[\]\]\s*\$Platform\s*=\s*@\((.*?)\)",
            self.source,
        )
        self.assertIsNotNone(platform_default, "Platform must have an array default")
        defaults = re.findall(r"[\"'](x64|x86|ARM64)[\"']", platform_default.group(1))
        self.assertEqual(defaults, ["x64", "x86", "ARM64"])

    def test_builds_only_the_new_winui_project(self) -> None:
        normalized = self.source.replace("/", "\\").casefold()
        self.assertIn("dev\\typedown.winui\\typedown.winui.csproj", normalized)
        self.assertNotIn("dev\\typedown\\typedown.csproj", normalized)
        self.assertNotIn("tools\\typedown.package", normalized)
        self.assertNotIn("typedown.sln", normalized)

    def test_has_explicit_restore_modes_without_installing_an_sdk(self) -> None:
        self.assertRegex(
            self.source,
            r"(?is)\[ValidateSet\([^\]]*[\"']Auto[\"'][^\]]*[\"']Always[\"'][^\]]*[\"']Never[\"'][^\]]*\)\]",
        )
        self.assertRegex(self.source, r"(?i)\bdotnet\b.*\brestore\b")
        self.assertRegex(
            self.source,
            r"(?is)\$buildArguments\s*=\s*@\([^)]*[\"']build[\"']",
        )
        self.assertRegex(
            self.source,
            r"(?i)&\s*\$dotnet\.Source\s+@buildArguments",
        )
        forbidden = (
            "dotnet-install",
            "winget",
            "choco",
            "invoke-webrequest",
            "start-bitstransfer",
            "packagecertificatethumbprint",
            "new-selfsignedcertificate",
            "import-pfxcertificate",
            "set-content",
            "copy-item",
            "remove-item",
            "git checkout",
            "git clean",
            "git reset",
        )
        folded = self.source.casefold()
        for command in forbidden:
            with self.subTest(command=command):
                self.assertNotIn(command, folded)

    def test_reports_provenance_and_propagates_a_failure(self) -> None:
        folded = self.source.casefold()
        self.assertIn("rev-parse", folded)
        for field in ("commit=", "configuration=", "platform="):
            with self.subTest(field=field):
                self.assertIn(field, folded)
        self.assertRegex(self.source, r"(?is)exit\s+\$[A-Za-z][A-Za-z0-9_]*Failure")


if __name__ == "__main__":
    unittest.main()
