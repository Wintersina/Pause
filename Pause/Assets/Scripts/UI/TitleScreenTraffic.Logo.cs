using UnityEngine;

// Home-screen traffic, part 5: the PAUSE logo flinches when a ship crashes
// into it.
//
// A ship diving into the lettering (PlungeImpact) kicks the logo a few
// pixels along the ship's flight line and tips it a couple of degrees, then
// it rattles back in a short damped shake, held on the 24 fps tick like the
// rest of the home screen's motion. When the shake ends the logo's
// transform is put back to exactly what it was (captured when the shake
// started): no drift, ever. A second crash mid-shake refreshes it rather
// than stacking: the energy goes up by one hit but never past LogoShakeCap,
// so even a pile of crashes stays a small shake. Only the logo's transform
// moves; its sprite, colour, sorting and art are never touched. Ships shot
// down (or sniped by the elite) never shake it -- only a real crash does.
// Leaving the home screen mid-shake (Shutdown) settles it at rest.
public partial class TitleScreenTraffic
{
    public const float LogoShakeTime = .42f;     // seconds, one hit
    public const float LogoShakeShift = .03f;    // world units at one hit's energy (~5-6 px on a phone)
    public const float LogoShakeTilt = 1.8f;     // degrees at one hit's energy
    public const float LogoShakeCap = 1.6f;      // most energy repeated hits can build (x one hit)
    public const float LogoShakeHz = 7f;         // rattle frequency

    Transform logoTf;
    Vector3 logoRestPos;
    Quaternion logoRestRot;
    bool logoShaking;
    float shakeT, shakeK, shakeSide;
    Vector2 shakeDir;

    // ---- stats, read by the tests
    public bool LogoShaking => logoShaking;
    public int LogoShakes { get; private set; }
    public float LogoShakeEnergy => logoShaking ? shakeK : 0f;
    public float PeakLogoShift { get; private set; }
    public float PeakLogoTilt { get; private set; }
    public Vector3 LogoRestPosition => logoRestPos;
    public Quaternion LogoRestRotation => logoRestRot;

    // dir: the way the crashing ship was flying; side: -1 hit left of centre, +1 right.
    public void ShakeLogo(Vector2 dir, float side)
    {
        if (logoRenderer == null) return;
        var tr = logoRenderer.transform;
        if (!logoShaking)
        {
            logoTf = tr;
            logoRestPos = tr.localPosition;
            logoRestRot = tr.localRotation;
            shakeK = 0f;
        }
        // refresh, don't stack: what is left of the shake plus one hit, capped
        shakeK = Mathf.Min(LogoShakeCap, ShakeEnvelope(shakeK, shakeT) + 1f);
        shakeT = 0f;
        shakeDir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.down;
        shakeSide = side < 0f ? -1f : 1f;
        logoShaking = true;
        LogoShakes++;
        PoseLogo();
    }

    static float ShakeEnvelope(float k, float t)
    {
        float u = 1f - Mathf.Clamp01(t / LogoShakeTime);
        return k * u * u;
    }

    void TickLogoShake(float dt)
    {
        if (!logoShaking) return;
        shakeT += dt;
        if (shakeT >= LogoShakeTime || logoTf == null) { SettleLogo(); return; }
        PoseLogo();
    }

    void PoseLogo()
    {
        if (logoTf == null) return;
        // held poses on the 24 fps tick, kicked along the hit first
        float t = Mathf.Floor(shakeT / Tick) * Tick;
        float env = ShakeEnvelope(shakeK, t);
        float osc = Mathf.Cos(t * LogoShakeHz * 2f * Mathf.PI);
        float tiltOsc = Mathf.Sin(t * LogoShakeHz * 2f * Mathf.PI + .9f);
        Vector2 off = shakeDir * (LogoShakeShift * env * osc);
        float tilt = -shakeSide * LogoShakeTilt * env * tiltOsc;
        logoTf.localPosition = logoRestPos + new Vector3(off.x, off.y, 0f);
        logoTf.localRotation = logoRestRot * Quaternion.Euler(0f, 0f, tilt);
        PeakLogoShift = Mathf.Max(PeakLogoShift, off.magnitude);
        PeakLogoTilt = Mathf.Max(PeakLogoTilt, Mathf.Abs(tilt));
    }

    // Back to exactly where it was.
    void SettleLogo()
    {
        if (logoShaking && logoTf != null)
        {
            logoTf.localPosition = logoRestPos;
            logoTf.localRotation = logoRestRot;
        }
        logoShaking = false;
        shakeK = 0f;
        shakeT = 0f;
    }
}
