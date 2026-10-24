using UnityEngine;

// The killer beat of the death crash: while the board is in slow motion
// (DeathCrashDomino.SlowMo) the camera eases in and drifts toward whatever
// killed the ship -- the tumbling killer piece, or for a shot / beam / hazard
// the shooter that fired it, or the boss body -- and a soft warm glow breathes
// behind it, so "YOU DIED BY <NAME>" (DeathCaption) lands on something the eye
// is already on. No outlines: the glow is a blurred disc behind the sprite.
// The weight runs on the crash's own clamped unscaled clock (ZoomDuration from
// the fatal hit), so Time.timeScale is never touched and coming back from the
// background resumes mid-beat.
//
// The zoom is applied in LateUpdate and taken back at the top of the next one
// (like the shake), as a multiplicative size change plus an additive shift, so
// a CameraFit pass in between does not leave a stale size behind. Every exit
// (Finish, skip, Cancel, Stop, pause, disable, scene teardown) calls
// EndZoom / UndoZoom. A killer that cannot be resolved, is already destroyed
// or is off screen leaves the beat as it was: no zoom, no glow.
public partial class DeathCrash
{
    // ---- intensity ----
    public const float ZoomDuration = MinTotal;   // fatal hit to the camera being back to normal
    public const float ZoomIn = .3f, ZoomOut = .4f;
    public const float ZoomSize = .74f;           // orthographic size at full zoom, x the normal size
    public const float ZoomPull = .6f;            // view centre: 0 = where the ship died, 1 = on the killer
    public const float ZoomMaxShift = 2.4f;       // furthest the view centre drifts from the camera, world units
    public const float GlowAlpha = .4f, GlowPulseHz = 3.5f, GlowScale = 2.8f;
    const int GlowTexSize = 64;

    SpriteRenderer glow;
    Transform zoomFocus;
    Vector3 zoomFocusAt, zoomShipAt, zoomShift;
    float zoomRatio = 1f, zoomWeight, zoomRadius;
    bool zoomOn;
    Camera zoomCam;
    static Sprite glowSprite;

    public bool ZoomActive => zoomOn;
    public float ZoomWeight => zoomWeight;
    public bool ZoomApplied => zoomCam != null;

    // Starts the beat for `killer` (the collider's object), ship died at `shipAt`.
    void BeginZoom(GameObject killer, Vector3 shipAt)
    {
        EndZoom();
        Transform focus = null;
        zoomRadius = .5f;
        for (int i = 0; i < pieceCount; i++)
            if (pieces[i].kind == PieceKind.Killer && pieces[i].active)
            {
                focus = pieces[i].tf;
                zoomRadius = pieces[i].radius;
                break;
            }
        if (focus == null) focus = FocusOf(killer);
        if (focus == null || !OnScreen(focus.position)) return;   // unresolved / gone / off screen: as before
        zoomFocus = focus;
        zoomFocusAt = focus.position;
        zoomShipAt = shipAt;
        zoomOn = true;
        zoomWeight = 0f;
        if (glow == null) BuildGlow();
        if (glow != null) glow.enabled = true;
    }

    bool OnScreen(Vector3 p)
    {
        return Mathf.Abs(p.x) <= edge + .4f && p.y >= viewBottom && p.y <= viewTop;
    }

    // The thing a hit came from: the shooter of a shot, beam or hazard; else
    // the body itself (a boss body, a rock, a mine). Runs once, at a death.
    static Transform FocusOf(GameObject hit)
    {
        if (hit == null) return null;
        EliteShotHitbox shotBox;
        if (hit.TryGetComponent(out shotBox) && shotBox.shot != null)
        {
            var s = shotBox.shot;
            if (s.Owner != null) return s.Owner.transform;
            return s.Shooter != null ? s.Shooter.transform : null;
        }
        AttackHazardHitbox hazardBox;
        if (hit.TryGetComponent(out hazardBox) && hazardBox.hazard != null)
            return hazardBox.hazard.Shooter != null ? hazardBox.hazard.Shooter.transform : null;
        RailMineLaserHitbox laserBox;
        if (hit.TryGetComponent(out laserBox) && laserBox.laser != null) return laserBox.laser.Owner;
        var bossShot = hit.GetComponentInParent<BossProjectile>();
        if (bossShot != null)   // a shot knows its boss only by def: the one boss on the board
        {
            var boss = Object.FindFirstObjectByType<BossActor>();
            return boss != null ? boss.transform : null;
        }
        var bossBeam = hit.GetComponentInParent<BossBeam>();
        if (bossBeam != null) return bossBeam.Owner != null ? bossBeam.Owner.transform : null;
        return hit.transform;
    }

    // This step of the beat: the weight and the glow (the camera follows in LateUpdate).
    void StepZoom()
    {
        if (!zoomOn) return;
        if (zoomFocus == null || elapsed >= ZoomDuration) { EndZoom(); return; }
        zoomFocusAt = zoomFocus.position;
        float inK = Mathf.SmoothStep(0f, 1f, elapsed / ZoomIn);
        float outK = Mathf.SmoothStep(0f, 1f, (ZoomDuration - elapsed) / ZoomOut);
        zoomWeight = Mathf.Min(inK, outK);
        if (glow == null) return;
        float pulse = .75f + .25f * Mathf.Sin(elapsed * GlowPulseHz * Mathf.PI * 2f);
        var c = AkiraPalette.Bone;
        c.a = GlowAlpha * zoomWeight * pulse;
        glow.color = c;
        glow.transform.position = zoomFocusAt;
        glow.transform.localScale = Vector3.one * (Mathf.Max(.3f, zoomRadius) * GlowScale);
    }

    // LateUpdate: eases the camera in for this frame.
    void ApplyZoom()
    {
        if (!zoomOn || zoomWeight <= 0.001f) return;
        var cam = Camera.main;
        if (cam == null || !cam.orthographic) return;
        Vector3 want = Vector3.Lerp(zoomShipAt, zoomFocusAt, ZoomPull);
        Vector3 at = cam.transform.position;
        Vector2 shift = new Vector2(want.x - at.x, want.y - at.y) * zoomWeight;
        shift = Vector2.ClampMagnitude(shift, ZoomMaxShift);
        zoomRatio = Mathf.Lerp(1f, ZoomSize, zoomWeight);
        zoomShift = new Vector3(shift.x, shift.y, 0f);
        cam.orthographicSize *= zoomRatio;
        cam.transform.position += zoomShift;
        zoomCam = cam;
    }

    // Takes the applied zoom back off the camera (idempotent).
    void UndoZoom()
    {
        if (zoomCam != null)
        {
            zoomCam.orthographicSize /= zoomRatio;
            zoomCam.transform.position -= zoomShift;
        }
        zoomCam = null;
        zoomRatio = 1f;
        zoomShift = Vector3.zero;
    }

    // The beat is over (or never began): camera back, glow off.
    void EndZoom()
    {
        zoomOn = false;
        zoomWeight = 0f;
        zoomFocus = null;
        UndoZoom();
        if (glow != null) glow.enabled = false;
    }

    void OnDisable() { UndoZoom(); }
    void OnApplicationPause(bool paused) { if (paused) UndoZoom(); }

    void BuildGlow()
    {
        if (glowSprite == null)
        {
            var tex = new Texture2D(GlowTexSize, GlowTexSize, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[GlowTexSize * GlowTexSize];
            float half = GlowTexSize * .5f;
            for (int y = 0; y < GlowTexSize; y++)
                for (int x = 0; x < GlowTexSize; x++)
                {
                    float d = Mathf.Sqrt((x + .5f - half) * (x + .5f - half) + (y + .5f - half) * (y + .5f - half)) / half;
                    float a = Mathf.Clamp01(1f - d);
                    a *= a;
                    px[y * GlowTexSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            glowSprite = Sprite.Create(tex, new Rect(0f, 0f, GlowTexSize, GlowTexSize), new Vector2(.5f, .5f), GlowTexSize);
        }
        var go = new GameObject("~KillerGlow", typeof(SpriteRenderer));
        go.transform.SetParent(transform, false);
        glow = go.GetComponent<SpriteRenderer>();
        glow.sprite = glowSprite;
        glow.sortingOrder = PieceOrder - 1;
        glow.enabled = false;
    }
}
