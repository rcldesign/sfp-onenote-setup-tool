"""Fake win32com.client.gencache: EnsureDispatch returns the same fake OneNote as Dispatch, or fails when
FAKE_NO_MAKEPY=1 so the late-binding fallback is exercised."""
import os

from . import Dispatch


def EnsureDispatch(progid):
    if os.environ.get("FAKE_NO_MAKEPY") == "1":
        raise RuntimeError("makepy unavailable (fake)")
    return Dispatch(progid)
