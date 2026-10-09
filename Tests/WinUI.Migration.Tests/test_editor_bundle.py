from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
WINUI_PROJECT = REPOSITORY_ROOT / "Dev" / "Typedown.WinUI" / "Typedown.WinUI.csproj"
EDITOR_CONFIG = REPOSITORY_ROOT / "Dev" / "Typedown.Editor" / "config-overrides.js"


class EditorBundleTests(unittest.TestCase):
    def test_winui_reuses_the_current_muya_bundle(self) -> None:
        project = WINUI_PROJECT.read_text(encoding="utf-8")
        editor_config = EDITOR_CONFIG.read_text(encoding="utf-8")

        self.assertIn("Typedown/Resources/Statics", editor_config.replace("\\\\", "/"))
        self.assertIn("EditorStaticBundleSource", project)
        self.assertIn("..\\Typedown\\Resources\\Statics", project)

    def test_msbuild_only_consumes_the_prebuilt_editor_bundle(self) -> None:
        source = WINUI_PROJECT.read_text(encoding="utf-8").casefold()

        self.assertNotIn("npm ", source)
        self.assertNotIn("yarn ", source)
        self.assertNotIn("exec command=", source)

    def test_editor_bundle_is_copied_to_output_and_msix(self) -> None:
        root = ET.parse(WINUI_PROJECT).getroot()
        targets = {target.attrib.get("Name"): target for target in root.findall(".//Target")}

        refresh = targets.get("RefreshEditorStaticBundle")
        package = targets.get("AddEditorStaticBundleToPackagingOutputs")
        self.assertIsNotNone(refresh)
        self.assertIsNotNone(package)
        self.assertFalse(
            refresh.findall(".//Content"),
            "A target can run repeatedly during packaging and must not append duplicate content",
        )

        package_source = ET.tostring(package, encoding="unicode")
        self.assertEqual(root.findtext(".//EditorStaticBundleTarget"), "Resources\\Statics")
        bundle_content = next(
            (
                item
                for item in root.findall(".//Content")
                if "$(EditorStaticBundleSource)" in item.attrib.get("Include", "")
            ),
            None,
        )
        self.assertIsNotNone(bundle_content)
        self.assertEqual(bundle_content.findtext("CopyToOutputDirectory"), "Always")
        self.assertEqual(bundle_content.findtext("CopyToPublishDirectory"), "Always")
        self.assertIn("$(EditorStaticBundleTarget)", bundle_content.findtext("Link") or "")
        self.assertIn("PackagingOutputs", package_source)
        self.assertIn("Resources\\Statics\\", package_source)
        self.assertIn("_ComputeAppxPackagePayload", package.attrib.get("BeforeTargets", ""))

    def test_runnable_and_packaged_builds_fail_when_index_is_missing(self) -> None:
        root = ET.parse(WINUI_PROJECT).getroot()
        targets = {target.attrib.get("Name"): target for target in root.findall(".//Target")}
        target = targets.get("RefreshEditorStaticBundle")
        self.assertIsNotNone(target)

        errors = target.findall("Error")
        self.assertTrue(errors, "A build can currently succeed without a usable editor")
        error_source = " ".join(ET.tostring(item, encoding="unicode") for item in errors)
        self.assertIn("index.html", error_source)
        self.assertIn("Release", error_source)
        self.assertIn("Debug_Local", error_source)


if __name__ == "__main__":
    unittest.main()
