"""Minimal App Store Connect API client (JWT ES256, stdlib urllib). Never prints the key."""
import hashlib
import json
import os
import time
import urllib.error
import urllib.request

BASE = "https://api.appstoreconnect.apple.com"


class AscError(Exception):
    pass


def credentials_from_env(env=os.environ):
    keys = ("ASC_KEY_ID", "ASC_ISSUER_ID", "ASC_KEY_PATH")
    vals = [env.get(k) for k in keys]
    if not all(vals):
        return None
    return dict(zip(keys, vals))


class Client:
    def __init__(self, key_id, issuer_id, key_path):
        self.key_id, self.issuer_id = key_id, issuer_id
        with open(os.path.expanduser(key_path), "r") as f:
            self._key = f.read()
        self._token, self._exp = None, 0

    def token(self):
        if self._token and time.time() < self._exp - 60:
            return self._token
        import jwt  # PyJWT, imported lazily so offline dry-runs and tests need nothing installed
        self._exp = int(time.time()) + 15 * 60
        self._token = jwt.encode(
            {"iss": self.issuer_id, "exp": self._exp, "aud": "appstoreconnect-v1"},
            self._key, algorithm="ES256", headers={"kid": self.key_id, "typ": "JWT"})
        return self._token

    def request(self, method, path, body=None):
        url = path if path.startswith("http") else BASE + path
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(url, data=data, method=method, headers={
            "Authorization": "Bearer " + self.token(), "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                raw = r.read()
        except urllib.error.HTTPError as e:
            raise AscError("%s %s -> %d %s" % (method, path, e.code, e.read().decode(errors="replace")[:800]))
        return json.loads(raw) if raw else {}

    def get_all(self, path):
        """Follow links.next; returns the combined data list."""
        out, url = [], path + ("&" if "?" in path else "?") + "limit=200"
        while url:
            d = self.request("GET", url)
            out += d.get("data", [])
            url = d.get("links", {}).get("next")
        return out

    def get_or_none(self, path):
        try:
            return self.request("GET", path).get("data")
        except AscError as e:
            if " 404 " in str(e):
                return None
            raise

    def upload_parts(self, operations, blob):
        """Run the reservation's uploadOperations (PUT byte ranges to the given urls)."""
        for op in operations:
            chunk = blob[op["offset"]: op["offset"] + op["length"]]
            headers = {h["name"]: h["value"] for h in op.get("requestHeaders", [])}
            req = urllib.request.Request(op["url"], data=chunk, method=op["method"], headers=headers)
            urllib.request.urlopen(req, timeout=120).read()


def md5(blob):
    return hashlib.md5(blob).hexdigest()
