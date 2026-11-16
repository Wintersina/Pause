using UnityEngine;

// COLLECT ALL's payout flourish: the Shop's star-dust burst (ShopDustFx, same sparkles and arc to the total) played once
// per find, sped up to the sequence's pace. A small fixed pool of bursts is built (and its images created) up front, so
// a find only re-arms one: no allocation, no load. Overlapping bursts are fine; when every slot is busy a new find simply
// skips its burst (the dust is credited either way), so the live particle count is capped at Slots * ParticlesPerBurst.
public sealed class CodexDustFx
{
    public const int Slots = 4, ParticlesPerBurst = 8;
    public const int MaxLiveParticles = Slots * ParticlesPerBurst;

    readonly ShopDustFx[] slots = new ShopDustFx[Slots];
    readonly int[] payload = new int[Slots];
    public int Spawned { get; private set; }

    // `parent` null (tests) keeps it image-free but the timeline still runs.
    public CodexDustFx(RectTransform parent, Sprite dust)
    {
        for (int i = 0; i < Slots; i++)
        {
            RectTransform layer = null;
            if (parent != null)
            {
                layer = CodexUi.NewRect("DustFx" + i, parent);
                CodexUi.Stretch(layer);
            }
            slots[i] = new ShopDustFx(layer, ParticlesPerBurst);
            slots[i].Warm(dust);
        }
    }

    public ShopDustFx Slot(int i) { return slots[i]; }

    public bool Active { get { for (int i = 0; i < Slots; i++) if (slots[i].Active) return true; return false; } }

    // Particles that can still be on screen right now.
    public int LiveParticles { get { int n = 0; for (int i = 0; i < Slots; i++) if (slots[i].Active) n += slots[i].Count; return n; } }

    // Counter pulse: the strongest of the running bursts (1 when idle).
    public float Pulse { get { float p = 1f; for (int i = 0; i < Slots; i++) if (slots[i].Active) p = Mathf.Max(p, slots[i].Pulse); return p; } }

    // Dust of the running bursts that has not landed yet (the counter holds these back so it ticks up as pieces arrive).
    public int InFlightDust
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Slots; i++) if (slots[i].Active) n += Mathf.Max(0, payload[i] - slots[i].Displayed);
            return n;
        }
    }

    // One burst for `dust` (a piece per dust, up to ParticlesPerBurst) at `origin`; the last piece lands on `target` after about
    // `landSeconds`. Layer-local points. False if no slot is free or there is nothing to show.
    public bool Spawn(Vector2 origin, Vector2 target, float landSeconds, int dust)
    {
        if (dust <= 0) return false;
        for (int i = 0; i < Slots; i++)
        {
            var s = slots[i];
            if (s.Active) continue;
            s.Speed = ShopDustFx.LastLandSeconds(Mathf.Min(ParticlesPerBurst, dust)) / Mathf.Max(.05f, landSeconds);
            payload[i] = dust;
            s.Begin(origin, target, 0, dust, null);
            Spawned++;
            return s.Active;
        }
        return false;
    }

    public void Step(float dt)
    {
        for (int i = 0; i < Slots; i++) if (slots[i].Active) slots[i].Step(dt);
    }

    // Skip / close: everything stops at once and nothing is left on screen.
    public void Stop()
    {
        for (int i = 0; i < Slots; i++) slots[i].Finish();
    }
}
