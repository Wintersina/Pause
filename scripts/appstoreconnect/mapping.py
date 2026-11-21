"""Pure data mapping (no network): local files -> App Store Connect request attributes."""
import csv
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
EXPORT = os.path.join(REPO, "docs", "achievements-export")
BUNDLE_ID = "me.hapticgate.pause"
LOCALE = "en-US"
GC_POINT_CAP = 1000
GC_ACHIEVEMENT_CAP = 100
IAP_NAME_MAX, IAP_DESC_MAX = 30, 45

# Allowed values (App Store Connect API enums).
IAP_TYPES = {"CONSUMABLE", "NON_CONSUMABLE", "NON_RENEWING_SUBSCRIPTION"}


def load_achievements(csv_path=None, image_dir=None):
    """Rows of docs/achievements-export/game-center.csv as ASC-ready dicts.

    The CSV's Image column names `<id>_1024.png`, but the committed files are `<id>.png`;
    the file is resolved from the achievement id suffix (`ach_<id>`).
    """
    csv_path = csv_path or os.path.join(EXPORT, "game-center.csv")
    image_dir = image_dir or os.path.join(EXPORT, "gamecenter-1024")
    out = []
    with open(csv_path, newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            vid = r["Achievement ID"].strip()
            short = vid.split(".ach_", 1)[1] if ".ach_" in vid else vid
            out.append({
                "vendorIdentifier": vid,
                "referenceName": r["Reference name"].strip(),
                "points": int(r["Point value"]),
                "showBeforeEarned": r["Hidden"].strip().lower() != "yes",  # hidden = not shown before earned
                "repeatable": r["Achievable more than once"].strip().lower() == "yes",
                "locale": LOCALE,
                "name": r["Title"].strip(),
                "beforeEarnedDescription": r["Pre-earned description"].strip(),
                "afterEarnedDescription": r["Earned description"].strip(),
                "image": os.path.join(image_dir, short + ".png"),
            })
    return out


def validate_achievements(rows):
    """Return a list of problems (empty = fine)."""
    bad = []
    ids = [r["vendorIdentifier"] for r in rows]
    if len(set(ids)) != len(ids):
        bad.append("duplicate achievement ids")
    total = sum(r["points"] for r in rows)
    if total > GC_POINT_CAP:
        bad.append("points total %d exceeds %d" % (total, GC_POINT_CAP))
    for r in rows:
        if not 0 < r["points"] <= GC_ACHIEVEMENT_CAP:
            bad.append("%s: points %d out of 1..%d" % (r["vendorIdentifier"], r["points"], GC_ACHIEVEMENT_CAP))
        if not r["vendorIdentifier"].startswith(BUNDLE_ID + "."):
            bad.append("%s: id lacks bundle prefix" % r["vendorIdentifier"])
        if not os.path.exists(r["image"]):
            bad.append("%s: missing image %s" % (r["vendorIdentifier"], r["image"]))
        if not (r["name"] and r["beforeEarnedDescription"] and r["afterEarnedDescription"]):
            bad.append("%s: empty text" % r["vendorIdentifier"])
    return bad


def achievement_attrs(row):
    return {k: row[k] for k in ("referenceName", "points", "showBeforeEarned", "repeatable")}


def achievement_loc_attrs(row):
    return {"locale": row["locale"], "name": row["name"],
            "beforeEarnedDescription": row["beforeEarnedDescription"],
            "afterEarnedDescription": row["afterEarnedDescription"]}


def top_score_board():
    """The only Game Center board (docs/leaderboards.md)."""
    return {
        "attrs": {
            "referenceName": "Top Score",
            "vendorIdentifier": BUNDLE_ID + ".top_score",
            "defaultFormatter": "INTEGER",
            "submissionType": "BEST_SCORE",
            "scoreSortType": "DESC",
        },
        "loc": {"locale": LOCALE, "name": "Top Score"},
    }


def load_iap(path=None):
    path = path or os.path.join(HERE, "data", "iap_products.json")
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    return d["locale"], d["products"]


def validate_iap(products):
    bad = []
    seen = set()
    for p in products:
        pid = p["productId"]
        if pid in seen:
            bad.append("duplicate product id " + pid)
        seen.add(pid)
        if p["type"] not in IAP_TYPES:
            bad.append("%s: bad type %s" % (pid, p["type"]))
        if len(p["displayName"]) > IAP_NAME_MAX:
            bad.append("%s: display name > %d" % (pid, IAP_NAME_MAX))
        if len(p["description"]) > IAP_DESC_MAX:
            bad.append("%s: description > %d" % (pid, IAP_DESC_MAX))
    return bad


def iap_attrs(p):
    return {"name": p["referenceName"], "productId": p["productId"],
            "inAppPurchaseType": p["type"], "reviewNote": p.get("reviewNote", ""),
            "familySharable": bool(p.get("familySharable", False))}


def iap_loc_attrs(locale, p):
    return {"locale": locale, "name": p["displayName"], "description": p["description"]}
