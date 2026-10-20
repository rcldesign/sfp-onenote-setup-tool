"""Fake win32api.GetFileVersionInfo."""


def GetFileVersionInfo(path, key):
    return {"FileVersionMS": (16 << 16) | 0, "FileVersionLS": (18827 << 16) | 20140}
