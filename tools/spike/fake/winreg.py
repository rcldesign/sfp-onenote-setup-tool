"""Fake winreg with the two keys m0_1_com.py reads."""
HKEY_CLASSES_ROOT = 0
_VALUES = {
    "OneNote.Application\\CLSID": "{F7BE9E27-2CB3-4E55-9F3A-32FB8A5F3F18}",
    "OneNote.Application.16\\CLSID": "{F7BE9E27-2CB3-4E55-9F3A-32FB8A5F3F18}",
    "CLSID\\{F7BE9E27-2CB3-4E55-9F3A-32FB8A5F3F18}\\LocalServer32": '"C:\\Program Files\\Microsoft Office\\root\\Office16\\ONENOTE.EXE" /Automation',
}


class _Key:
    def __init__(self, path):
        self.path = path

    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False


def OpenKey(root, path):
    if path not in _VALUES:
        raise OSError(2, "not found", path)
    return _Key(path)


def QueryValue(key, name):
    return _VALUES[key.path]
