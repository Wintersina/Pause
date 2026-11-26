using UnityEngine;

// Extra companions use the ship's own gun art in wings outside the central heart orbit.
// Both the resting bodies and their full hover ranges need that clearance: otherwise
// several heart styles can never find room for a loop or turn-back. Recruits occupy
// outer columns; DroneFormation folds them inside the view near screen edges.
public class FloatieCompanion : MonoBehaviour
{
    public const float SideReach = 3.2f;     // leave the hearts' orbit clear inside the extra wings
    public const float SlotOneRise = .85f;    // x the hull's half height, above the middle
    public const float SlotTwoDrop = 1f;      // x the hull's half height, below the middle
    public const float OuterReach = 4.4f;     // recruits sit outside the opening wings
    public const float OuterDrop = .35f, OuterRise = .2f;   // x the hull's half height
    public const float HoverShare = .15f;      // of the main floatie's hover drift: they keep to their own corner

    public const float TurnDegreesPerSecond = 420f;   // how fast it swings to face its aim
    int ship, slot;
    Transform hull;
    SpriteRenderer sr;
    float aim, aimTarget;     // degrees off straight up (+ is to the left), as the bullet's angle
    float bodyScale, kick, arrivalAge = 1f;
    Vector3 arrivalFrom;
    bool folded;
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
        switch (slot)
        {
            case 0: rest = new Vector3(-side * e.x * SideReach, e.y * SlotOneRise, .02f); break;
            case 1: rest = new Vector3(side * e.x * SideReach, -e.y * SlotTwoDrop, .02f); break;
            case 2: rest = new Vector3(-side * e.x * SideReach, -e.y * SlotTwoDrop, .02f); break;
            case 3: rest = new Vector3(side * e.x * OuterReach, -e.y * OuterDrop, .02f); break;
            default:
                float wing = slot % 2 == 0 ? -side : side;
                int row = (slot - 4) / 2;
                float height = row % 2 == 0 ? (row / 2 + 1) * .75f : -(row / 2 + 1) * .75f;
                rest = new Vector3(wing * e.x * (OuterReach + .7f), e.y * height, .02f);
                break;
        }

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

    public Bounds WorldRestEnvelope() { return folded && sr != null ? sr.bounds : ShipUiSlots.GunToWorld(hull, ship, LocalRestEnvelope()); }

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

    public void Kick() { kick = .12f; Apply(); }
    public void JoinFrom(Vector3 from) { arrivalFrom = from; arrivalAge = 0f; }

    public void StepAim(float dt)
    {
        kick = Mathf.Max(0f, kick - dt);
        if (dt > 0f) aim = Mathf.MoveTowardsAngle(aim, aimTarget, TurnDegreesPerSecond * dt);
        Apply();
    }

    void Apply()
    {
        if (sr != null)
        {
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, aim);
            sr.transform.localPosition = Vector3.down * bodyScale * .08f * (kick / .12f);
        }
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
        if (sr != null) transform.position = DroneFormation.KeepVisible(hull, transform.position, sr.bounds, out folded);
        if (arrivalAge < .7f)
        {
            arrivalAge += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(arrivalAge / .7f));
            transform.position = Vector3.Lerp(arrivalFrom, transform.position, u);
            transform.localScale = Vector3.one * Mathf.Lerp(.4f, 1f, u);
        }
    }

    void LateUpdate() { Place(); }

    void OnDestroy() { ShipUiSlots.Unregister(this); }
}

// Runs the floaties' small guns for one ship: how many there are (by weapon level), their cooldowns, when they hold fire, and the
// shots. Attached by ShipPowerController next to the UltimateGun. Public Step so edit-mode tests can drive it with a real dt.
public class FloatieBattery : MonoBehaviour
{
    public enum Hold { None, Frozen, Dead, Transition, Entrance, Tutorial }   // (the ultimate no longer holds the gun)

    int ship;
    UltimateGun gun;
    ShipAttackRunner runner;
    readonly FloatieCompanion[] extras = new FloatieCompanion[FloatieTuning.MaxFloaties - 1];
    readonly float[] timer = new float[FloatieTuning.MaxFloaties];
    int level = -1, count, roundRobin, squadVersion = -1;
    readonly float[] upgradeGlow = new float[FloatieTuning.MaxFloaties];
    readonly int[] celebrationShots = new int[FloatieTuning.MaxFloaties];
    readonly bool[] sweepLeft = new bool[FloatieTuning.MaxFloaties];
    int levelOverride = -1;
    Color tint = Color.white;
    int previewSlot = -1;
    float previewUntil;
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

    // The battery of the ship in play (the upgrade popup names a drone through it); null when there is none.
    public static FloatieBattery Current { get; private set; }

    // "LEFT DRONE": where drone `index` hovers. The main floatie is on the ship's own side (even ids left, odd right); with three or more
    // drones the name also says its row (TOP / LOW / OUTER) so no two share a name.
    public string DroneName(int index)
    {
        bool mainLeft = ship % 2 == 0;
        string[] sides = { mainLeft ? "LEFT" : "RIGHT", mainLeft ? "RIGHT" : "LEFT" };
        string side = sides[index % 2 == 0 ? 0 : 1];
        if (count <= 2) return side + " DRONE";
        string[] rows = { "TOP", "TOP", "LOW", "LOW", "OUTER", "OUTER" };
        return side + " " + rows[Mathf.Clamp(index, 0, 5)] + " DRONE";
    }

    public static FloatieBattery Attach(GameObject shipObject, int shipId, UltimateGun companion)
    {
        var existing = shipObject.GetComponent<FloatieBattery>();
        if (existing != null) return existing;
        var b = shipObject.AddComponent<FloatieBattery>();
        b.Build(shipId, companion);
        Current = b;
        return b;
    }

    void Build(int shipId, UltimateGun companion)
    {
        ship = shipId;
        gun = companion;
        tint = WeaponStyleTable.For(shipId).energy;
        FloatieBullets.Prebuild(FloatieTuning.PoolSize);
        FloatieFx.Prebuild();
        GunImpactFeedback.Prewarm();   // the three hit clips and their voices, built now, not on the first hit of a fight
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
        squadVersion = DroneSquad.Version;
        count = DroneSquad.Slots;
        ShotAtoms.SetDroneCount(count);
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
        if (index == 0) return cd;
        // spread the rest over the wait so they never all go off at once: index 1 at the half, then eighths
        return index == 1 ? cd * .5f : cd * (.25f + .1f * ((index - 2) % 4));
    }

    public void ResetTimers()
    {
        for (int i = 0; i < count; i++) timer[i] = Stagger(i);
    }

    void Update() { Step(ShipAttackRunner.ScaledDelta()); }

    void OnDestroy()
    {
        if (Current == this) Current = null;
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
        return Hold.None;
    }

    // The ultimate is sliding out, firing or its attack is still running (information only: it no longer holds the gun).
    public bool UltimateBusy
    {
        get
        {
            if (gun != null && gun.Busy) return true;
            if (ShipPowerController.CinematicClearActive) return true;
            if (runner == null) TryGetComponent(out runner);
            return runner != null && runner.ActiveRuns > 0;
        }
    }

    // ---- the step -------------------------------------------------------

    public void Step(float dt)
    {
        var hold = ComputeHold(dt);
        CurrentHold = hold;
        if (level < 0 || squadVersion != DroneSquad.Version) Refresh();

        bool gone = hold == Hold.Dead || hold == Hold.Transition || hold == Hold.Tutorial;
        for (int slot = 0; slot < extras.Length; slot++)
        {
            var e = extras[slot];
            if (e != null) e.gameObject.SetActive(count > slot + 1 && hold != Hold.Dead);
        }
        if (gone) { FloatieBullets.ReleaseAll(this); return; }

        FloatieFx.Tick(dt);
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
        // ROUND-ROBIN: the floatie that is looked at first rotates every step, so when several are due in the same step (and the bullet cap
        // is close) the first one does not always get the room.
        roundRobin = (roundRobin + 1) % Mathf.Max(1, count);
        for (int j = 0; j < count; j++)
        {
            int i = (roundRobin + j) % count;
            if (!DroneSquad.IsActive(i)) continue;
            float cd = FloatieTuning.Cooldown(i, level, boss);
            if (timer[i] > cd) timer[i] = cd;   // a boss's 5 s never stretches a wait already running; leaving it never lengthens one
            timer[i] -= dt;
            if (i > 0) AimExtra(i, timer[i], dt);
            else if (gun != null)
            {
                float look = 0f;
                if (DroneSquad.Mode(0) == DroneMode.Aimed && FloatieAim.Nearest(gun.DroneMuzzle, FloatieTuning.SeekerRange, out var primaryPick))
                { var d = primaryPick.at - gun.DroneMuzzle; look = Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg; }
                else if (DroneSquad.Mode(0) == DroneMode.Alternating) look = sweepLeft[0] ? -25f : 25f;
                gun.AimDrone(look);
            }
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
        var mode = DroneSquad.Mode(index);
        if (mode == DroneMode.Alternating)
            e.AimAt(timeLeft <= AimLead ? (sweepLeft[index] ? -25f : 25f) : 0f);
        else if (mode == DroneMode.Aimed)
        {
            FloatieAim.Pick pick;
            if (FloatieAim.Nearest(e.Muzzle, FloatieTuning.SeekerRange, out pick))
            {
                Vector3 to = pick.at - e.Muzzle;
                e.AimAt(Mathf.Atan2(-to.x, to.y) * Mathf.Rad2Deg);
            }
            else e.AimAt(0f);
        }
        else e.AimAt(0f);
        e.StepAim(dt);
    }

    void RollPending()
    {
        if (pendingSet) return;
        pendingAngle = FloatieTuning.SpreadAngle(Random.value, Random.value);
        pendingSet = true;
    }

    // The bullets of floatie `index` in the air now.
    public int LiveBy(int index)
    {
        int n = 0;
        var all = FloatieBullets.All;
        for (int i = 0; i < all.Count; i++) if (all[i].Active && all[i].Owner == this && all[i].Source == index) n++;
        return n;
    }

    public Vector3 MuzzleFor(int index) => MuzzleOf(index);
    public void PreviewDrone(int index) { previewSlot = index; previewUntil = Time.unscaledTime + .15f; }

    Vector3 MuzzleOf(int index)
    {
        if (index > 0 && extras[index - 1] != null) return extras[index - 1].Muzzle;
        if (gun != null && gun.BarrelRenderer != null)
        {
            return gun.DroneMuzzle;
        }
        return transform.position + Vector3.up * .5f;
    }

    // One shot from floatie `index` (0-based). False when no bullet was free.
    public bool Fire(int index)
    {
        if (index < 0 || index >= count || !DroneSquad.IsActive(index)) return false;
        Vector3 from = MuzzleOf(index);
        Vector3 heading = Vector3.up;
        var pick = default(FloatieAim.Pick);
        float angle = 0f;
        var companion = index > 0 ? extras[index - 1] : null;
        if (DroneSquad.Mode(index) == DroneMode.Alternating)
        {
            angle = sweepLeft[index] ? -25f : 25f;
            sweepLeft[index] = !sweepLeft[index];
            if (companion != null) { companion.AimAt(angle, true); from = companion.Muzzle; }
            heading = Quaternion.Euler(0f, 0f, angle) * Vector3.up;
        }
        else if (FloatieTuning.IsSeeker(index))
        {
            // the seeker AIMS, then fires straight: its bullet never turns after it leaves
            bool locked = FloatieAim.Nearest(from, FloatieTuning.SeekerRange, out pick);
            LastSeekerLocked = locked;
            if (locked)
            {
                // turn the floatie to face the target, then fire along that facing (the nose moves a little as it turns: twice settles it)
                for (int pass = 0; pass < 2; pass++)
                {
                    heading = pick.at - from;
                    angle = Mathf.Atan2(-heading.x, heading.y) * Mathf.Rad2Deg;
                    if (companion == null)
                    {
                        if (gun != null) { gun.AimDrone(angle); from = gun.DroneMuzzle; }
                        continue;
                    }
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
        // (PER DRONE: this floatie's own levels; only RAPID is shared by all of them)
        int twin = ShotAtoms.Level(index, ShotAtom.Twin);
        int pierce = ShotAtoms.Level(index, ShotAtom.Pierce), split = ShotAtoms.Level(index, ShotAtom.Split);
        int ricochet = ShotAtoms.Level(index, ShotAtom.Ricochet), arc = ShotAtoms.Level(index, ShotAtom.Arc);
        int n = ShotAtomTuning.TwinShots[twin];
        float[] angles = ShotAtomTuning.TwinAngles[twin], offsets = ShotAtomTuning.TwinOffsets[twin];
        // a near-straight shot leans a little with RICOCHET, so it can reach a side edge at all (not a seeker's locked one)
        float lean = 0f;
        if (ricochet > 0 && pick.lockOn == null && Mathf.Abs(angle) < ShotAtomTuning.RicochetStraightWithin)
            lean = Random.value < .5f ? ShotAtomTuning.RicochetLeanDegrees : -ShotAtomTuning.RicochetLeanDegrees;
        Vector3 side = new Vector3(heading.y, -heading.x, 0f).normalized;
        // FAIR SHARE: the volley leaves room for every other floatie that has nothing in the air (they are about to fire), but always
        // fires at least one bullet.
        int roomLeft = FloatieTuning.PoolSize - FloatieBullets.Active, waiting = 0;
        for (int d = 0; d < count; d++) if (d != index && DroneSquad.IsActive(d) && LiveBy(d) == 0) waiting++;
        n = Mathf.Min(n, Mathf.Max(1, roomLeft - waiting));
        int fired = 0;
        for (int k = 0; k < n; k++)
        {
            var bullet = FloatieBullets.Take(true);
            if (bullet == null) break;   // at the bullet cap: the rest of the volley is not fired
            var mods = FloatieBullet.Mods.Plain;
            mods.damage = ShotAtomTuning.TwinDamage[twin];
            mods.pierce = ShotAtomTuning.PierceThrough[pierce];
            mods.bounces = ShotAtomTuning.Bounces[ricochet];
            mods.life = ShotAtomTuning.RicochetLife[ricochet];   // 0: the plain lifetime
            mods.shards = split;
            mods.arc = arc;
            mods.source = index;
            mods.tier = GunBoltArt.Tier(index);
            mods.celebrated = celebrationShots[index] > 0;
            mods.rapid = ShotAtoms.Level(index, ShotAtom.Rapid) > 0;
            Vector3 h = Quaternion.Euler(0f, 0f, angles[k] + lean) * heading;
            bullet.Launch(this, ship, from + side * offsets[k], h, tint, pick, mods);
            if (twin > 0 && k < 2) FloatieFx.Spark(FloatieFx.Kind.Muzzle, from + side * offsets[k], h, tint);
            fired++;
        }
        if (fired == 0) return false;
        FloatieFx.Spark(celebrationShots[index] > 0 ? FloatieFx.Kind.UpgradeMuzzle : FloatieFx.Kind.Muzzle, from, heading, tint);
        if (companion != null) companion.Kick();
        LastAngle[index] = angle;
        Shots[index]++;
        if (celebrationShots[index] > 0) celebrationShots[index]--;
        return true;
    }

    public void JoinDrone(int index, Vector3 from)
    {
        Refresh();
        if (index > 0 && extras[index - 1] != null) extras[index - 1].JoinFrom(from);
        NotifyUpgrade(index);
    }

    public void NotifyUpgrade(int index)
    {
        if (index < 0 || index >= count) return;
        upgradeGlow[index] = 1.2f; celebrationShots[index] = 3; timer[index] = 0f;
    }

    void LateUpdate()
    {
        for (int i = 0; i < count; i++)
        {
            upgradeGlow[i] = Mathf.Max(0f, upgradeGlow[i] - Time.unscaledDeltaTime);
            var sr = i == 0 ? (gun != null ? gun.BarrelRenderer : null) : (extras[i - 1] != null ? extras[i - 1].Renderer : null);
            if (sr == null || (i == 0 && gun.Busy)) continue;
            Color colour = DroneSquad.IsActive(i) ? Color.Lerp(tint, DroneSquad.Colour(DroneSquad.Mode(i)), .35f) : new Color(.24f, .3f, .36f, .45f);
            float highlight = upgradeGlow[i] > 0f ? .5f + .5f * Mathf.Sin(upgradeGlow[i] * 32f) : (previewSlot == i && Time.unscaledTime < previewUntil ? .35f : 0f);
            sr.color = Color.Lerp(colour, Color.white, highlight);
            if (i == 0 && gun != null) gun.DroneTint = sr.color;
        }
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
