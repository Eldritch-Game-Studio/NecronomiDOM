"""Behavior checks for local links, offline external URLs and Markdown code examples."""

import importlib.util
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("check_docs", Path(__file__).with_name("check-docs.py"))
docs = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(docs)


class DocumentationTests(unittest.TestCase):
    def fixture(self, content):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "README.md").write_text(content, encoding="utf-8")
        return root

    def test_local_reference_images_and_external_links_work_offline(self):
        root = self.fixture('# Docs\n\n[local](file%20name.txt) ![picture][img]\n'
                            '[img]: picture.png\n[external](https://example.invalid/page#part)\n'
                            '<https://example.invalid/other> [heading](#docs)\n')
        (root / "file name.txt").touch()
        (root / "picture.png").touch()
        self.assertEqual(docs.check_markdown(root, "README.md"), 5)

    def test_missing_link_and_malformed_external_url_fail(self):
        for content, error in [("[missing](absent.png)", "Missing local"),
                               ("[bad](https:///absent)", "Malformed external")]:
            root = self.fixture(content)
            with self.subTest(content=content), self.assertRaisesRegex(ValueError, error):
                docs.check_markdown(root, "README.md")

    def test_examples_are_not_repository_links_and_unclosed_fences_fail(self):
        root = self.fixture('```md\n[example](not-a-real-file)\n```\n`[inline](example)`\n')
        self.assertEqual(docs.check_markdown(root, "README.md"), 0)
        (root / "README.md").write_text("```md\nunclosed", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Unclosed"):
            docs.check_markdown(root, "README.md")


if __name__ == "__main__":
    unittest.main()
