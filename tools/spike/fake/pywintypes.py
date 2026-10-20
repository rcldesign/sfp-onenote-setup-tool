"""Fake pywintypes: the com_error shape pywin32 raises (hresult, strerror, excepinfo, argerror)."""


class com_error(Exception):  # noqa: N801
    def __init__(self, hresult, strerror, excepinfo, argerror):
        super().__init__(hresult, strerror, excepinfo, argerror)
        self.hresult = hresult
        self.strerror = strerror
        self.excepinfo = excepinfo
        self.argerror = argerror
