import unittest
from pathlib import Path
from xml.etree import ElementTree


REPO_ROOT = Path(__file__).resolve().parents[2]
WINUI_ROOT = REPO_ROOT / "Dev" / "Typedown.WinUI"
CONTRACTS_ROOT = REPO_ROOT / "Dev" / "Typedown.Contracts"
CORE_ROOT = REPO_ROOT / "Dev" / "Typedown.Core.Net10"
PROJECT_FILE = WINUI_ROOT / "Typedown.WinUI.csproj"
COMPOSITION_FILE = WINUI_ROOT / "Services" / "ServiceConfiguration.cs"
LOCALIZER_FILE = WINUI_ROOT / "Services" / "WinUIStringLocalizer.cs"
CATALOG_FILE = CORE_ROOT / "Localization" / "ReswStringCatalog.cs"
CONTRACT_FILE = CONTRACTS_ROOT / "Localization" / "IStringLocalizer.cs"


class LocalizationResourceTests(unittest.TestCase):
    def test_existing_translations_are_linked_without_copying_them(self) -> None:
        project = ElementTree.parse(PROJECT_FILE).getroot()
        resources = [
            item
            for item in project.findall(".//Content")
            if "Resources\\Strings" in item.attrib.get("Include", "")
        ]
        self.assertEqual(len(resources), 1)
        resource = resources[0]
        self.assertTrue(resource.attrib["Include"].startswith("..\\Typedown.Core"))
        self.assertEqual(
            resource.findtext("Link"),
            r"Resources\Strings\%(RecursiveDir)%(Filename)%(Extension)",
        )
        self.assertEqual(resource.findtext("CopyToOutputDirectory"), "PreserveNewest")
        self.assertEqual(resource.findtext("CopyToPublishDirectory"), "PreserveNewest")

    def test_localization_contract_and_platform_neutral_catalog_exist(self) -> None:
        contract = CONTRACT_FILE.read_text(encoding="utf-8")
        catalog = CATALOG_FILE.read_text(encoding="utf-8")

        self.assertRegex(contract, r"interface\s+IStringLocalizer\b")
        self.assertIn("LanguageChanged", contract)
        self.assertIn("LocalizationResourceSource", contract)
        self.assertRegex(catalog, r"class\s+ReswStringCatalog\b")
        for forbidden in ("Microsoft.UI", "Windows.UI", "Windows.ApplicationModel"):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, catalog)

    def test_one_process_localizer_tracks_the_shared_settings_store(self) -> None:
        localizer = LOCALIZER_FILE.read_text(encoding="utf-8")
        composition = COMPOSITION_FILE.read_text(encoding="utf-8")

        self.assertRegex(localizer, r"class\s+WinUIStringLocalizer\s*:\s*IStringLocalizer")
        self.assertIn('LanguageSettingName = "Language"', localizer)
        self.assertIn("settings.Changed +=", localizer)
        self.assertRegex(composition, r"AddSingleton\s*<\s*ReswStringCatalog\s*>")
        self.assertRegex(composition, r"AddSingleton\s*<\s*IStringLocalizer")


if __name__ == "__main__":
    unittest.main()
