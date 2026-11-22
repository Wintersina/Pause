using UnityEngine;

// An extra floatie (FloatieGuns.cs): the ship's own companion art (UltimateGun's roster sheet), hovering the same way, in a slot of
// its own so it never covers the hull, the hearts' orbit (HeartOrbit asks the battery for these spots), the charge indicator in
// front of the nose, the exhaust or the thumb's pad below. Slot 1 is the second floatie, slot 2 the seeker.
//
//   main floatie (UltimateGun)  beside the hull on its own side (even ids left, odd right), a little above the middle
//   slot 1  the OTHER side, up by the nose   (-side * SideReach hull-widths, +SlotOneRise hull-heights)
//   slot 2  the main's side, low             (+side * SideReach,             -SlotTwoDrop)
public class FloatieCompanion : MonoBehaviour
{
    public const float SideReach = 1.22f;     // x the hull's half width (the main floatie sits at the same reach)
    public const float SlotOneRise = .85f;    // x the hull's half height, above the middle
    public const float SlotTwoDrop = 1f;      // x the hull's half height, below the middle
    public const float HoverShare = .6f;      // of the main floatie's hover drift: they keep to their own corner

    public const float TurnDegreesPerSecond = 420f;   // how fast it swings to face its aim
    int ship, slot;
    Transform hull;
    SpriteRenderer sr;
    float aim, aimTarget;     // degrees off straight up (+ is to the left), as the bullet's angle
    float bodyScale;
    Vector3 rest;

    public int Slot => slot;
    public SpriteRenderer Renderer => sr;
    public Vector3 RestLocal => rest;
    public bool Shown => sr != null && sr.enabled && gameObject.activeInHierarchy;

    public static FloatieCompanion Create(Transform hullTransform, int shipId, int slotIndex)
    {
        var go = new GameObject("~Floatie" + (slotIndex + 1));
        go.transform.SetParent(hullTransform, false);
        var c = go.AddComponent<FloatieCompanion>();
        c.Build(hullTransform, shipId, slotIndex);
        return c;
    }

    void Build(Transform hullTransform, int shipId, int slotIndex)
    {
        hull = hullTransform;
        ship = shipId;
        slot = slotIndex;
        var hr = hull.GetComponent<SpriteRenderer>();
        Vector2 e = hr != null && hr.sprite != null ? (Vector2)hr.sprite.bounds.extents : new Vector2(.4f, .5f);
        float side = ship % 2 == 0 ? -1f : 1f;   // the main floatie's side
        rest = slot == 0
            ? new Vector3(-side * e.x * SideReach, e.y * SlotOneRise, .02f)
            : new Vector3(side * e.x * SideReach, -e.y * SlotTwoDrop, .02f);

        var body = new GameObject("Body", typeof(SpriteRenderer));
        body.transform.SetParent(transform, false);
        sr = body.GetComponent<SpriteRenderer>();
        sr.sprite = UltimateGun.CompanionSprite(ship);
        sr.sortingOrder = (hr != null ? hr.sortingOrder : 0) + 1;
        bodyScale = 1f;
        if (sr.sprite != null)
        {
            float fit = Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y);
            bodyScale = (e.y * .72f) / Mathf.Max(.0001f, fit);   // the main floatie's size
        }
        body.transform.localScale = Vector3.one * bodyScale;
        Place();
        ShipUiSlots.Register(hull, this, () => ShipUiSlots.GunToWorld(hull, ship, LocalRestEnvelope()),
                             () => ShipUiSlots.DrawnBounds(transform));
    }

    Vector3 Hover()
    {
        // each slot its own hover behaviour (the roster's four), and a calmer drift
        return UltimateGun.HoverAt(ship + 1 + slot, Time.time + slot * 1.7f) * HoverShare;
    }

    // Hull-local: every place its drawing can be (the whole hover drift), for the hearts and the other ship UI.
    public Bounds LocalRestEnvelope()
    {
        Vector2 a = UltimateGun.HoverAmplitude(ship + 1 + slot) * HoverShare;
        Vector3 size = new Vector3(a.x * 2f, a.y * 2f, 0f);
        Vector3 center = rest;
        if (sr != null && sr.sprite != null)
        {
            var sb = sr.sprite.bounds;
            center += sb.center * bodyScale;
            size += sb.size * bodyScale;
        }
        center.z = 0f;
        return new Bounds(center, size);
    }

    public Bounds WorldRestEnvelope() { return ShipUiSlots.GunToWorld(hull, ship, LocalRestEnvelope()); }

    // Where a bullet leaves it: the top of the drawing (the nose of the barrel, turned with it).
    public Vector3 Muzzle
    {
        get
        {
            if (sr == null) return transform.position;
            Bounds sb = sr.sprite != null ? sr.sprite.bounds : new Bounds(Vector3.zero, Vector3.one);
            Vector3 p = sr.transform.TransformPoint(new Vector3(sb.center.x, sb.max.y, 0f));
            p.z = 0f;
            return p;
        }
    }

    // The way it faces now (the bullet's direction when it fires), and its angle off straight up (+ left).
    public float Aim => aim;
    public float AimTarget => aimTarget;
    public Vector3 FacingDir => Quaternion.Euler(0f, 0f, aim) * Vector3.up;

    // Turn to face `degrees` (eased by StepAim); `snap` faces it at once (the instant it fires).
    public void AimAt(float degrees, bool snap = false)
    {
        aimTarget = degrees;
        if (snap) { aim = degrees; Apply(); }
    }

    public void StepAim(float dt)
    {
        if (dt > 0f) aim = Mathf.MoveTowardsAngle(aim, aimTarget, TurnDegreesPerSecond * dt);
        Apply();
    }

    void Apply()
    {
        if (sr != null) sr.transform.localRotation = Quaternion.Euler(0f, 0f, aim);
    }

    public void Place()
    {
        if (hull == null) return;
        Vector3 target = rest + Hover();
        if (ShipUiSlots.Spins(ship))
        {
            // Ninja and UFO spin their whole hull: stay upright beside it, as the main floatie does.
            transform.position = hull.position + Vector3.Scale(target, hull.lossyScale);
            transform.rotation = Quaternion.identity;
        }
        else transform.localPosition = target;
    }

    void LateUpdate() { Place(); }

    void OnDestroy() { ShipUiSlots.Unregister(this); }
}

// Runs the floaties' small guns for one ship: how many there are (by weapon level), their cooldowns, when they hold fire, and the
// shots. Attached by ShipPowerController next to the UltimateGun. Public Step so edit-mode tests can drive it with a real dt.
public class FloatieBattery : MonoBehaviour
{
    public enum Hold { None, Frozen, Dead, Transition, Entrance, Tutorial, Ultimate }

    int ship;
    UltimateGun gun;
    ShipAttackRunner runner;
    readonly FloatieCompanion[] extras = new FloatieCompanion[2];
    readonly float[] timer = new float[FloatieTuning.MaxFloaties];
    int level = -1, count;
    int levelOverride = -1;
    Color tint = Color.white;
    const float AimLead = .3f;   // floatie #2 tilts to its next angle this long before the shot
    float pendingAngle;
    bool pendingSet;

    // Counters and the last shot's numbers (tests, previews).
    public readonly int[] Shots = new int[FloatieTuning.MaxFloaties];
    public readonly float[] LastAngle = new float[FloatieTuning.MaxFloaties];   // degrees off straight up (+ left)
    public bool LastSeekerLocked { get; private set; }
    public Hold CurrentHold { get; private set; }

    public int Count => count;
    public int Level => level;
    public int Ship => ship;
    public float Timer(int index) { return timer[index]; }
    public FloatieCompanion Extra(int slot) { return slot >= 0 && slot < extras.Length ? extras[slot] : null; }
    public bool BossActive => BossEncounter.Running;
    public float Cooldown(int index) { return FloatieTuning.Cooldown(index, level, BossActive); }

    public static FloatieBattery Attach(GameObject shipObject, int shipId, UltimateGun companion)
    {
        var existing = shipObject.GetComponent<FloatieBattery>();
        if (existing != null) return existing;
        var b = shipObject.AddComponent<FloatieBattery>();
        b.Build(shipId, companion);
        return b;
    }

    void Build(int shipId, UltimateGun companion)
    {
        ship = shipId;
        gun = companion;
        tint = WeaponStyleTable.For(shipId).energy;
        FloatieBullets.Prebuild(8);
        Refresh();
    }

    // Re-reads the weapon level (a colour bought) and adds or hides floaties to match. A new one starts staggered.
    public void Refresh()
    {
        SetLevel(levelOverride >= 0 ? levelOverride : ShipWeaponUpgrades.Level(ship));
    }

    // Tests and previews: fly at this level whatever the saved skins say.
    public void SetLevel(int newLevel)
    {
        levelOverride = newLevel;
        newLevel = Mathf.Clamp(newLevel, 0, ShipWeaponUpgrades.MaxLevel);
        int before = count;
        level = newLevel;
        count = FloatieTuning.CountAt(level);
        for (int slot = 0; slot < extras.Length; slot++)
        {
            bool want = count > slot + 1;
            if (want && extras[slot] == null) extras[slot] = FloatieCompanion.Create(transform, ship, slot);
            if (extras[slot] != null) extras[slot].gameObject.SetActive(want && CurrentHold != Hold.Dead);
        }
        for (int i = before; i < count; i++) timer[i] = Stagger(i);
        if (before == 0) timer[0] = Stagger(0);
        for (int i = count; i < timer.Length; i++) timer[i] = 0f;
    }

    // A fresh floatie waits its cooldown, the second half of it and the seeker a quarter of it, so they do not all go off at once.
    float Stagger(int index)
    {
        float cd = FloatieTuning.Cooldown(index, level, BossActive);
        return index == 1 ? cd * .5f : index == 2 ? cd * .25f : cd;
    }

    public void ResetTimers()
    {
        for (int i = 0; i < count; i++) timer[i] = Stagger(i);
    }

    void Update() { Step(ShipAttackRunner.ScaledDelta()); }

    void OnDestroy()
    {
        FloatieBullets.ReleaseAll(this);
        for (int i = 0; i < extras.Length; i++)
            if (extras[i] != null)
            {
                if (Application.isPlaying) Object.Destroy(extras[i].gameObject);
                else Object.DestroyImmediate(extras[i].gameObject);
            }
    }

    // ---- when it holds fire ---------------------------------------------

    public Hold ComputeHold(float dt)
    {
        if (ShipId.TutorialFlight) return Hold.Tutorial;
        if (buttonClicks.playerDied || DeathCrash.Running) return Hold.Dead;
        if (WorldTransition.InProgress) return Hold.Transition;
        if (BossEncounter.ScriptedEntrance) return Hold.Entrance;
        // the world is frozen (a pause: finger up with pauses left)
        if (dt <= 0f || Time.timeScale <= 0f || (!TouchInput.IsPressed && score.pauseCounter > 0)) return Hold.Frozen;
        if (UltimateBusy) return Hold.Ultimate;
        return Hold.None;
    }

    // The ultimate is sliding out, firing or its attack is still running: the floatie is busy with it.
    public bool UltimateBusy
    {
        get
        {
            if (gun != null && gun.Busy) return true;
            if (ShipPowerController.CinematicClearActive) return true;
            if (runner == null) runner = GetComponent<ShipAttackRunner>();
            return runner != null && runner.ActiveRuns > 0;
        }
    }

    // ---- the step -------------------------------------------------------

    public void Step(float dt)
    {
        var hold = ComputeHold(dt);
        CurrentHold = hold;
        if (level < 0) Refresh();

        bool gone = hold == Hold.Dead || hold == Hold.Transition || hold == Hold.Tutorial;
        for (int slot = 0; slot < extras.Length; slot++)
        {
            var e = extras[slot];
            if (e != null) e.gameObject.SetActive(count > slot + 1 && hold != Hold.Dead);
        }
        if (gone) { FloatieBullets.ReleaseAll(this); return; }

        // bullets already in the air fly on (not while the world is frozen: dt is 0 then)
        var all = FloatieBullets.All;
        for (int i = 0; i < all.Count; i++)
        {
            var b = all[i];
            if (b.Active && b.Owner == this) b.Tick(dt);
        }
        if (hold != Hold.None)
        {
            for (int slot = 0; slot < extras.Length; slot++)
                if (extras[slot] != null) { extras[slot].AimAt(0f); extras[slot].StepAim(dt); }
            return;   // timers hold, nothing is lost
        }

        bool boss = BossActive;
        for (int i = 0; i < count; i++)
        {
            float cd = FloatieTuning.Cooldown(i, level, boss);
            if (timer[i] > cd) timer[i] = cd;   // a boss's 5 s never stretches a wait already running; leaving it never lengthens one
            timer[i] -= dt;
            if (i > 0) AimExtra(i, timer[i], dt);
            if (timer[i] > 0f) continue;
            if (Fire(i)) timer[i] = cd;
            else timer[i] = .15f;   // no bullet free right now: try again in a moment
        }
    }

    // Floatie #2 tilts to its next random angle just before the shot; the seeker tracks its nearest target all the time (and eases
    // back to straight up with none).
    void AimExtra(int index, float timeLeft, float dt)
    {
        var e = extras[index - 1];
        if (e == null) return;
        if (index == 1)
        {
            if (timeLeft <= AimLead) { RollPending(); e.AimAt(pendingAngle); }
            else e.AimAt(0f);
        }
        else
        {
            FloatieAim.Pick pick;
            if (FloatieAim.Nearest(e.Muzzle, FloatieTuning.SeekerRange, out pick))
            {
                Vector3 to = pick.at - e.Muzzle;
                e.AimAt(Mathf.Atan2(-to.x, to.y) * Mathf.Rad2Deg);
            }
            else e.AimAt(0f);
        }
        e.StepAim(dt);
    }

    void RollPending()
    {
        if (pendingSet) return;
        pendingAngle = FloatieTuning.SpreadAngle(Random.value, Random.value);
        pendingSet = true;
    }

    Vector3 MuzzleOf(int index)
    {
        if (index > 0 && extras[index - 1] != null) return extras[index - 1].Muzzle;
        if (gun != null && gun.BarrelRenderer != null)
        {
            Bounds b = gun.BarrelRenderer.bounds;
            return new Vector3(b.center.x, b.max.y, 0f);
        }
        return transform.position + Vector3.up * .5f;
    }

    // One shot from floatie `index` (0-based). False when no bullet was free.
    public bool Fire(int index)
    {
        Vector3 from = MuzzleOf(index);
        Vector3 heading = Vector3.up;
        var pick = default(FloatieAim.Pick);
        float angle = 0f;
        var companion = index > 0 ? extras[index - 1] : null;
        if (index == 1)
        {
            RollPending();
            angle = pendingAngle;
            pendingSet = false;
            if (companion != null) { companion.AimAt(angle, true); from = companion.Muzzle; }
            heading = Quaternion.Euler(0f, 0f, angle) * Vector3.up;
        }
        else if (index >= 2)
        {
            bool locked = FloatieAim.Nearest(from, FloatieTuning.SeekerRange, out pick);
            LastSeekerLocked = locked;
            if (locked)
            {
                // turn the floatie to face the target, then fire along that facing (the nose moves a little as it turns: twice settles it)
                for (int pass = 0; pass < 2; pass++)
                {
                    heading = pick.at - from;
                    angle = Mathf.Atan2(-heading.x, heading.y) * Mathf.Rad2Deg;
                    if (companion == null) break;
                    companion.AimAt(angle, true);
                    from = companion.Muzzle;
                }
                heading = pick.at - from;
            }
            else
            {
                pick = default(FloatieAim.Pick);
                if (companion != null) companion.AimAt(0f, true);
            }
        }
        // The volley (the shot atoms, ShotAtoms.cs): TWIN's bullets, each carrying PIERCE / RICOCHET / SPLIT / ARC. Level 0 of
        // every atom is the one plain bullet.
        int twin = ShotAtoms.Level(ShotAtom.Twin);
        int pierce = ShotAtoms.Level(ShotAtom.Pierce), split = ShotAtoms.Level(ShotAtom.Split);
        int ricochet = ShotAtoms.Level(ShotAtom.Ricochet), arc = ShotAtoms.Level(ShotAtom.Arc);
        int n = ShotAtomTuning.TwinShots[twin];
        float[] angles = ShotAtomTuning.TwinAngles[twin], offsets = ShotAtomTuning.TwinOffsets[twin];
        // a near-straight shot leans a little with RICOCHET, so it can reach a side edge at all (not a seeker's locked one)
        float lean = 0f;
        if (ricochet > 0 && pick.lockOn == null && Mathf.Abs(angle) < ShotAtomTuning.RicochetStraightWithin)
            lean = Random.value < .5f ? ShotAtomTuning.RicochetLeanDegrees : -ShotAtomTuning.RicochetLeanDegrees;
        Vector3 side = new Vector3(heading.y, -heading.x, 0f).normalized;
        int fired = 0;
        for (int k = 0; k < n; k++)
        {
            var bullet = FloatieBullets.Take();
            if (bullet == null) break;   // at the bullet cap: the rest of the volley is not fired
            var mods = FloatieBullet.Mods.Plain;
            mods.damage = ShotAtomTuning.TwinDamage[twin];
            mods.pierce = ShotAtomTuning.PierceThrough[pierce];
            mods.bounces = ShotAtomTuning.Bounces[ricochet];
            mods.shards = split;
            mods.arc = arc;
            Vector3 h = Quaternion.Euler(0f, 0f, angles[k] + lean) * heading;
            bullet.Launch(this, ship, from + side * offsets[k], h, tint, pick, mods);
            fired++;
        }
        if (fired == 0) return false;
        LastAngle[index] = angle;
        Shots[index]++;
        return true;
    }

    // World boxes of the extra floaties' resting places (HeartOrbit hurries past them like the main floatie's); the count.
    public int ExtraRestBounds(Bounds[] into)
    {
        int n = 0;
        for (int i = 0; i < extras.Length && n < into.Length; i++)
            if (extras[i] != null && extras[i].gameObject.activeSelf) into[n++] = extras[i].WorldRestEnvelope();
        return n;
    }
}
