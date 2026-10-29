import unittest

from server import normalize_windows_path, project_name


class WindowsPathTests(unittest.TestCase):
    def test_removes_verbatim_drive_prefix(self):
        self.assertEqual(normalize_windows_path(r"\\?\C:\DEV\Project"), r"C:\DEV\Project")

    def test_converts_verbatim_unc_prefix(self):
        self.assertEqual(normalize_windows_path(r"\\?\UNC\server\share\Project"), r"\\server\share\Project")

    def test_keeps_regular_path(self):
        self.assertEqual(normalize_windows_path(r"C:\DEV\Project"), r"C:\DEV\Project")

    def test_project_name_uses_normalized_path(self):
        self.assertEqual(project_name(r"\\?\C:\DEV\Project"), "Project")


if __name__ == "__main__":
    unittest.main()
