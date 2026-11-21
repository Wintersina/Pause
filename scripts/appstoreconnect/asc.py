#!/usr/bin/env python3
"""Set up Pause's App Store Connect content. Dry-run by default; --apply writes.

  python3 -I scripts/appstoreconnect/asc.py check
  python3 -I scripts/appstoreconnect/asc.py achievements [--apply] [--only ach_id,...]
  python3 -I scripts/appstoreconnect/asc.py leaderboards [--apply]
  python3 -I scripts/appstoreconnect/asc.py iap [--apply]

Env: ASC_KEY_ID, ASC_ISSUER_ID, ASC_KEY_PATH (.p8 outside the repo). Without them a dry-run
prints the plan from local data only. Nothing here ever deletes anything.
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # -I removes the script dir

import mapping as M  # noqa: E402
from client import AscError, Client, credentials_from_env, md5  # noqa: E402


class Ctx:
    def __init__(self, api, apply):
        self.api, self.apply = api, apply
        self.counts = {"create": 0, "update": 0, "same": 0, "skip": 0}

    def log(self, verb, what):
        self.counts[verb] = self.counts.get(verb, 0) + 1
        mark = {"create": "+", "update": "~", "same": "=", "skip": "-"}[verb]
        print("  %s %-7s %s%s" % (mark, verb, what, "" if (self.apply or verb in ("same", "skip")) else "  (dry-run)"))

    def summary(self):
        c = self.counts
        print("\n%s: %d created, %d updated, %d unchanged, %d skipped%s" % (
            "APPLIED" if self.apply else "DRY-RUN", c["create"], c["update"], c["same"], c["skip"],
            "" if self.apply else "  (re-run with --apply to write)"))


def rel(rtype, rid):
    return {"data": {"type": rtype, "id": rid}}


def differs(existing_attrs, wanted):
    return {k: v for k, v in wanted.items() if existing_attrs.get(k) != v}


def upsert(ctx, label, existing, rtype, path, attrs, relationships=None, patch_keys=None):
    """Create if `existing` is None, else PATCH only changed attributes. Returns the id (None in dry-run create)."""
    if existing is None:
        ctx.log("create", label)
        if ctx.apply:
            body = {"data": {"type": rtype, "attributes": attrs}}
            if relationships:
                body["data"]["relationships"] = relationships
            return ctx.api.request("POST", path, body)["data"]["id"]
        return None
    wanted = {k: v for k, v in attrs.items() if patch_keys is None or k in patch_keys}
    diff = differs(existing["attributes"], wanted)
    if not diff:
        ctx.log("same", label)
        return existing["id"]
    ctx.log("update", "%s %s" % (label, sorted(diff)))
    if ctx.apply:
        ctx.api.request("PATCH", "%s/%s" % (path, existing["id"]),
                        {"data": {"type": rtype, "id": existing["id"], "attributes": diff}})
    return existing["id"]


def find_app(api):
    apps = api.request("GET", "/v1/apps?filter[bundleId]=" + M.BUNDLE_ID).get("data", [])
    return apps[0] if apps else None


def need_app(ctx):
    """Returns the app resource, or None (offline / not yet created)."""
    if not ctx.api:
        return None
    app = find_app(ctx.api)
    if not app:
        print("App record for %s not found. Create it in App Store Connect first (see docs)." % M.BUNDLE_ID)
        if ctx.apply:
            sys.exit(2)
    return app


def game_center_detail(ctx, app):
    if not (ctx.api and app):
        return None
    d = ctx.api.get_or_none("/v1/apps/%s/gameCenterDetail" % app["id"])
    if d:
        return d
    ctx.log("create", "gameCenterDetail (enable Game Center for the app)")
    if ctx.apply:
        return ctx.api.request("POST", "/v1/gameCenterDetails", {"data": {
            "type": "gameCenterDetails", "attributes": {}, "relationships": {"app": rel("apps", app["id"])}}})["data"]
    return None


def cmd_check(ctx, args):
    app = need_app(ctx)
    if not ctx.api:
        print("No credentials in env (ASC_KEY_ID / ASC_ISSUER_ID / ASC_KEY_PATH): nothing to check online.")
        return 0
    if not app:
        return 1
    a = app["attributes"]
    print("App: %s  bundle=%s  sku=%s  id=%s" % (a.get("name"), a.get("bundleId"), a.get("sku"), app["id"]))
    d = ctx.api.get_or_none("/v1/apps/%s/gameCenterDetail" % app["id"])
    if not d:
        print("Game Center: not enabled yet (the achievements/leaderboards commands will create the detail).")
        return 0
    print("Game Center detail id=%s attrs=%s" % (d["id"], d["attributes"]))
    for name, sub in (("achievements", "gameCenterAchievements"), ("leaderboards", "gameCenterLeaderboards")):
        print("  %s: %d" % (name, len(ctx.api.get_all("/v1/gameCenterDetails/%s/%s" % (d["id"], sub)))))
    print("  in-app purchases: %d" % len(ctx.api.get_all("/v1/apps/%s/inAppPurchasesV2" % app["id"])))
    return 0


def upload_image(ctx, loc_id, path, label):
    """Reserve, PUT the parts, commit with uploaded=true. Resumes an image stuck in AWAITING_UPLOAD."""
    img = None
    if loc_id and ctx.api:
        # the relationship endpoint omits uploadOperations, so fetch the image by id when we must resume
        have = ctx.api.get_or_none("/v1/gameCenterAchievementLocalizations/%s/gameCenterAchievementImage" % loc_id)
        if have:
            state = (have["attributes"].get("assetDeliveryState") or {}).get("state", "?")
            if state != "AWAITING_UPLOAD":
                ctx.log("same", "%s image (%s)" % (label, state))
                return
            img = ctx.api.request("GET", "/v1/gameCenterAchievementImages/" + have["id"])["data"]
    ctx.log("create", "%s image %s%s" % (label, os.path.basename(path), " (resume)" if img else ""))
    if not ctx.apply:
        return
    with open(path, "rb") as f:
        blob = f.read()
    if img is None:
        img = ctx.api.request("POST", "/v1/gameCenterAchievementImages", {"data": {
            "type": "gameCenterAchievementImages",
            "attributes": {"fileName": os.path.basename(path), "fileSize": len(blob)},
            "relationships": {"gameCenterAchievementLocalization": rel("gameCenterAchievementLocalizations", loc_id)}}})["data"]
    ctx.api.upload_parts(img["attributes"]["uploadOperations"], blob)
    ctx.api.request("PATCH", "/v1/gameCenterAchievementImages/" + img["id"], {"data": {
        "type": "gameCenterAchievementImages", "id": img["id"], "attributes": {"uploaded": True}}})


def cmd_achievements(ctx, args):
    rows = M.load_achievements()
    if args.only:
        want = {"%s.ach_%s" % (M.BUNDLE_ID, s) if "." not in s else s for s in args.only.split(",")}
        rows = [r for r in rows if r["vendorIdentifier"] in want]
    problems = M.validate_achievements(M.load_achievements())
    if problems:
        print("Local data problems:\n  " + "\n  ".join(problems))
        return 1
    print("%d achievements, %d points total (cap %d)" % (len(rows), sum(r["points"] for r in rows), M.GC_POINT_CAP))
    app = need_app(ctx)
    detail = game_center_detail(ctx, app)
    existing = {}
    if ctx.api and detail:
        existing = {x["attributes"]["vendorIdentifier"]: x
                    for x in ctx.api.get_all("/v1/gameCenterDetails/%s/gameCenterAchievements" % detail["id"])}
    for r in rows:
        label = r["vendorIdentifier"].split(".ach_", 1)[1]
        ex = existing.get(r["vendorIdentifier"])
        attrs = dict(M.achievement_attrs(r), vendorIdentifier=r["vendorIdentifier"])
        aid = upsert(ctx, label, ex, "gameCenterAchievements", "/v1/gameCenterAchievements", attrs,
                     {"gameCenterDetail": rel("gameCenterDetails", detail["id"])} if detail else None,
                     patch_keys=("referenceName", "points", "showBeforeEarned", "repeatable"))
        loc_ex = None
        if ex and ctx.api:
            for l in ctx.api.get_all("/v1/gameCenterAchievements/%s/localizations" % aid):
                if l["attributes"]["locale"] == r["locale"]:
                    loc_ex = l
        loc_id = upsert(ctx, label + " [en-US]", loc_ex, "gameCenterAchievementLocalizations",
                        "/v1/gameCenterAchievementLocalizations", M.achievement_loc_attrs(r),
                        {"gameCenterAchievement": rel("gameCenterAchievements", aid)} if aid else None,
                        patch_keys=("name", "beforeEarnedDescription", "afterEarnedDescription"))
        upload_image(ctx, loc_id, r["image"], label)
    return 0


def cmd_leaderboards(ctx, args):
    b = M.top_score_board()
    app = need_app(ctx)
    detail = game_center_detail(ctx, app)
    ex = None
    if ctx.api and detail:
        for x in ctx.api.get_all("/v1/gameCenterDetails/%s/gameCenterLeaderboards" % detail["id"]):
            if x["attributes"]["vendorIdentifier"] == b["attrs"]["vendorIdentifier"]:
                ex = x
    lid = upsert(ctx, "leaderboard top_score", ex, "gameCenterLeaderboards", "/v1/gameCenterLeaderboards",
                 b["attrs"], {"gameCenterDetail": rel("gameCenterDetails", detail["id"])} if detail else None,
                 patch_keys=("referenceName", "defaultFormatter", "scoreSortType", "submissionType"))
    loc_ex = None
    if ex and ctx.api:
        for l in ctx.api.get_all("/v1/gameCenterLeaderboards/%s/localizations" % lid):
            if l["attributes"]["locale"] == b["loc"]["locale"]:
                loc_ex = l
    upsert(ctx, "top_score [en-US]", loc_ex, "gameCenterLeaderboardLocalizations",
           "/v1/gameCenterLeaderboardLocalizations", b["loc"],
           {"gameCenterLeaderboard": rel("gameCenterLeaderboards", lid)} if lid else None,
           patch_keys=("name",))
    print("\nManual: add the board to the app version's Game Center section (and set it default); see the doc.")
    return 0


def cmd_iap(ctx, args):
    locale, products = M.load_iap()
    problems = M.validate_iap(products)
    if problems:
        print("Local data problems:\n  " + "\n  ".join(problems))
        return 1
    app = need_app(ctx)
    existing = {}
    if ctx.api and app:
        existing = {x["attributes"]["productId"]: x for x in ctx.api.get_all("/v1/apps/%s/inAppPurchasesV2" % app["id"])}
    for p in products:
        ex = existing.get(p["productId"])
        iid = upsert(ctx, p["productId"], ex, "inAppPurchases", "/v2/inAppPurchases", M.iap_attrs(p),
                     {"app": rel("apps", app["id"])} if app else None,
                     patch_keys=("name", "reviewNote", "familySharable"))
        loc_ex = None
        if ex and ctx.api:
            for l in ctx.api.get_all("/v2/inAppPurchases/%s/inAppPurchaseLocalizations" % iid):
                if l["attributes"]["locale"] == locale:
                    loc_ex = l
        upsert(ctx, p["productId"] + " [%s]" % locale, loc_ex, "inAppPurchaseLocalizations",
               "/v1/inAppPurchaseLocalizations", M.iap_loc_attrs(locale, p),
               {"inAppPurchaseV2": rel("inAppPurchases", iid)} if iid else None, patch_keys=("name", "description"))
        print("      manual: price ~USD %s, review screenshot, availability" % p["usdPrice"])
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["check", "achievements", "leaderboards", "iap"])
    ap.add_argument("--apply", action="store_true", help="write to App Store Connect (default: dry-run)")
    ap.add_argument("--only", help="achievements: comma list of ids (ach_x or short names)")
    args = ap.parse_args(argv)
    creds = credentials_from_env()
    api = Client(creds["ASC_KEY_ID"], creds["ASC_ISSUER_ID"], creds["ASC_KEY_PATH"]) if creds else None
    if args.apply and not api:
        print("--apply needs ASC_KEY_ID, ASC_ISSUER_ID and ASC_KEY_PATH.")
        return 2
    if not api and args.command != "check":
        print("[offline] no credentials: planning from local data only; every item shows as create.")
    ctx = Ctx(api, args.apply)
    try:
        rc = {"check": cmd_check, "achievements": cmd_achievements, "leaderboards": cmd_leaderboards,
              "iap": cmd_iap}[args.command](ctx, args)
    except AscError as e:
        print("API error: %s" % e)
        return 3
    if args.command != "check":
        ctx.summary()
    return rc


if __name__ == "__main__":
    sys.exit(main())
