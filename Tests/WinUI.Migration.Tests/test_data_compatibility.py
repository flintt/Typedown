from pathlib import Path
import json
import re
import sqlite3
import unittest
import xml.etree.ElementTree as ET


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
LEGACY_CORE = REPOSITORY_ROOT / "Dev" / "Typedown.Core"
NET10_CORE_PROJECT = (
    REPOSITORY_ROOT / "Dev" / "Typedown.Core.Net10" / "Typedown.Core.Net10.csproj"
)
LEGACY_CORE_PROJECT = LEGACY_CORE / "Typedown.Core.csproj"
PATH_CONTRACT = (
    REPOSITORY_ROOT
    / "Dev"
    / "Typedown.Contracts"
    / "Platform"
    / "IAppDataPathProvider.cs"
)
WINUI_PATH_PROVIDER = (
    REPOSITORY_ROOT
    / "Dev"
    / "Typedown.WinUI"
    / "Services"
    / "WinUIAppDataPathProvider.cs"
)
FIXTURES = REPOSITORY_ROOT / "Tests" / "Typedown.ReliabilityTests" / "Fixtures"


PERSISTENT_PATHS = {
    "SettingsFilePath": "Settings.json",
    "DatabaseFilePath": "Storage.db",
    "BackupDirectory": "Backup",
    "ThemesDirectory": "themes",
    "SessionFilePath": "session.json",
    "CursorFilePath": "cursors.json",
    "ImageUploadHistoryFilePath": "ImageUploadHistory.json",
    "HedgeDocSharesFilePath": "hedgedoc-shares.json",
}

LEGACY_PATH_OWNERS = {
    "Settings.json": LEGACY_CORE / "ViewModels" / "SettingsViewModel.cs",
    "Storage.db": LEGACY_CORE / "Services" / "AppDbContext.cs",
    "Backup": LEGACY_CORE / "Services" / "AutoBackup.cs",
    "themes": LEGACY_CORE / "Utilities" / "ThemeFiles.cs",
    "session.json": LEGACY_CORE / "Services" / "SessionMemory.cs",
    "cursors.json": LEGACY_CORE / "Services" / "CursorMemory.cs",
    "ImageUploadHistory.json": LEGACY_CORE / "Services" / "ImageUpload.cs",
    "hedgedoc-shares.json": LEGACY_CORE / "Services" / "HedgeDocShareMemory.cs",
}

EXPECTED_DATABASE_SCHEMA = {
    "ExportConfig": ("Id", "Name", "Notes", "Type", "Config"),
    "FileAccessHistory": ("Id", "AccessTime", "FilePath"),
    "FolderAccessHistory": ("Id", "AccessTime", "FolderPath"),
    "ImageUploadConfig": ("Id", "Name", "Notes", "IsEnable", "Method", "Config"),
    "__EFMigrationsHistory": ("MigrationId", "ProductVersion"),
}


def source(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def package_versions(project_file: Path) -> dict[str, str]:
    root = ET.parse(project_file).getroot()
    versions = {}
    for element in root.iter():
        if element.tag.rsplit("}", 1)[-1] != "PackageReference":
            continue
        name = element.attrib.get("Include") or element.attrib.get("Update")
        if not name:
            continue
        version = element.attrib.get("Version")
        if version is None:
            version_element = next(
                (
                    child
                    for child in element
                    if child.tag.rsplit("}", 1)[-1] == "Version"
                ),
                None,
            )
            version = (version_element.text or "").strip() if version_element is not None else ""
        versions[name] = version
    return versions


class DataCompatibilityTests(unittest.TestCase):
    def test_winui_path_contract_names_every_existing_persistent_file(self) -> None:
        contract = source(PATH_CONTRACT)
        provider = source(WINUI_PATH_PROVIDER)

        for property_name, file_name in PERSISTENT_PATHS.items():
            with self.subTest(property_name=property_name):
                self.assertRegex(contract, rf"\bstring\s+{property_name}\s*\{{\s*get;\s*\}}")
                self.assertRegex(provider, rf"\bpublic\s+string\s+{property_name}\s*\{{\s*get;\s*\}}")
                self.assertRegex(
                    provider,
                    rf"{property_name}\s*=\s*Path\.Combine\s*\(\s*PersistentDataDirectory\s*,\s*\"{re.escape(file_name)}\"\s*\)",
                )

    def test_winui_keeps_the_stable_packaged_and_unpacked_data_roots(self) -> None:
        legacy = source(LEGACY_CORE / "Config.cs")
        provider = source(WINUI_PATH_PROVIDER)

        for implementation in (legacy, provider):
            self.assertIn("ApplicationData.Current.LocalFolder.Path", implementation)
            self.assertIn("Environment.SpecialFolder.MyDocuments", implementation)

        self.assertIn("Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppName)", legacy)
        self.assertIn("CombineSpecialFolder(Environment.SpecialFolder.MyDocuments, appName)", provider)
        self.assertNotRegex(
            provider,
            r"(?i)Path\.Combine\s*\([^;]*[\"'](?:Typedown\.)?WinUI[\"']",
        )

    def test_path_names_are_taken_from_the_stable_implementation(self) -> None:
        for file_name, owner in LEGACY_PATH_OWNERS.items():
            with self.subTest(file_name=file_name):
                self.assertTrue(owner.is_file(), f"Missing stable path owner: {owner}")
                self.assertIn(f'"{file_name}"', source(owner))

    def test_legacy_settings_fixture_remains_a_newtonsoft_store_fixture(self) -> None:
        fixture = json.loads(source(FIXTURES / "legacy-settings.json"))
        self.assertEqual(18.5, fixture["FontSize"])
        self.assertIs(True, fixture["AutoSave"])
        self.assertEqual("zh-Hans", fixture["Language"])
        self.assertEqual(
            {"token": "keep-me", "enabled": True},
            fixture["FutureFeature"],
            "The unknown-field fixture is the guard against destructive settings rewrites",
        )

        legacy_packages = package_versions(LEGACY_CORE_PROJECT)
        net10_packages = package_versions(NET10_CORE_PROJECT)
        self.assertEqual("13.0.3", legacy_packages.get("Newtonsoft.Json"))
        self.assertEqual("13.0.3", net10_packages.get("Newtonsoft.Json"))
        self.assertIn("JsonSettingsStore.cs", source(NET10_CORE_PROJECT))
        self.assertNotIn("System.Text.Json", source(LEGACY_CORE / "Services" / "JsonSettingsStore.cs"))

    def test_legacy_storage_fixture_locks_table_column_and_migration_names(self) -> None:
        database = sqlite3.connect(":memory:")
        try:
            database.executescript(source(FIXTURES / "legacy-schema.sql"))
            table_names = {
                row[0]
                for row in database.execute(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"
                )
            }
            self.assertEqual(set(EXPECTED_DATABASE_SCHEMA), table_names)
            for table_name, expected_columns in EXPECTED_DATABASE_SCHEMA.items():
                with self.subTest(table=table_name):
                    columns = tuple(
                        row[1]
                        for row in database.execute(
                            f"PRAGMA table_info('{table_name.replace(chr(39), chr(39) * 2)}')"
                        )
                    )
                    self.assertEqual(expected_columns, columns)

            migration = database.execute(
                "SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory"
            ).fetchone()
            self.assertEqual(("20230226122314_InitialCreate", "3.1.30"), migration)
            self.assertEqual(
                (r"C:\docs\legacy.md", "2024-01-02 03:04:05"),
                database.execute(
                    "SELECT FilePath, AccessTime FROM FileAccessHistory WHERE Id=11"
                ).fetchone(),
            )
        finally:
            database.close()

        migration_source = source(
            LEGACY_CORE / "Migrations" / "20230226122314_InitialCreate.cs"
        )
        snapshot_source = source(LEGACY_CORE / "Migrations" / "DatabaseModelSnapshot.cs")
        for table_name, columns in EXPECTED_DATABASE_SCHEMA.items():
            if table_name == "__EFMigrationsHistory":
                continue
            with self.subTest(production_table=table_name):
                self.assertIn(f'name: "{table_name}"', migration_source)
                self.assertIn(f'b.ToTable("{table_name}")', snapshot_source)
                for column in columns:
                    self.assertRegex(migration_source, rf"\b{re.escape(column)}\s*=")

        legacy_packages = package_versions(LEGACY_CORE_PROJECT)
        net10_packages = package_versions(NET10_CORE_PROJECT)
        self.assertEqual("3.1.32", legacy_packages.get("Microsoft.EntityFrameworkCore.Sqlite"))
        self.assertIn(
            net10_packages.get("Microsoft.EntityFrameworkCore.Sqlite"),
            (None, "3.1.32"),
            "The UI migration must not silently upgrade EF or rewrite Storage.db",
        )


if __name__ == "__main__":
    unittest.main()
