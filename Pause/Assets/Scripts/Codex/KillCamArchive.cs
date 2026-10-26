using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// The codex's LAST STAND: the kill cam of the latest death that set a new
// best score, kept on the device so it can be rewatched (CodexReplayViewer,
// the Pilot's Log entry CodexId). One slot: each new high-score death
// replaces the one before.
//
// WHEN. playerIsDead settles the run; a ranked run (RunScore.SavesBest) that
// beat the best it started with (RunScore.IsNewBest) saves the tape's last
// LiveWindow seconds before the fatal hit, the hit and the death after it
// (ShouldSave). Practice, developer and tutorial runs never do.
//
// WHAT. The live tape refers to sprites and materials of the running scene,
// which the menu does not have, so a saved clip carries its own pictures:
//   * every distinct sprite in the clip is copied through the GPU into atlas
//     pages (PageSize square at most, MaxPages of them; one pass of quads and
//     one read-back a page) at the density it is SEEN at: its size on screen
//     (its sprite's size times the largest scale any of its entries is drawn
//     at -- a 1-unit boss cell drawn 3.3 u wide is copied 3.3 u wide) times
//     Density texels a world unit (the viewer's own resolution; LargeDensity
//     for backdrop plates wider than LargeExtent), never more than the source
//     has, less (a step at a time) if the pages would overflow. A shrinking
//     copy is box-filtered (Hidden/Pause/KillCamDownsample: thin lines of the
//     art dim, they never drop out) and every slot's edge texels are extruded
//     Pad texels into its gutter, so a sprite sampled at its edge never picks
//     up the clear gutter (a seam: a hairline across the screen where two
//     backdrop plates meet). Pages are stored as PNGs;
//   * a material other than the default sprite one is stored as its shader's
//     name and its float / colour / vector settings (re-made on load when
//     the shader is in the build -- the additive glows, the rails'); but a
//     shader that works in its sprite's own texture space or per renderer
//     (BakedShaders: the Space sky's wrap, the turning planets' disc, the
//     exhaust skins' palette shift) would sample other sprites in the clip's
//     atlas or lose its renderer's settings, so such a sprite is drawn through
//     its material -- its renderer's property block too -- into the atlas
//     instead and replayed plain;
//   * each rail (a scrolling quad) as its bounds, settings, a copy of its
//     texture (MeshTextureMax) and its offset per frame;
//   * frames StoreGap apart (the replay blends between them; the fatal hit's
//     frame always kept), positions relative to the camera.
// Then gzip.
//
// FILE. Application.persistentDataPath/FileName: "KCAM", the format version,
// then the gzipped payload, whose head repeats the record. Written to a temp
// file then moved over the old one, off the main thread (the PNG encode and
// gzip run in a Task: only the GPU copy happens on the main thread, once,
// as the Flight Complete panel opens). PrefsKey holds the record ("2|score|
// utc ticks|ship|world|killer") so the codex can describe it without opening
// the file. It is written only once the file is (Settle, on the main thread),
// so it never names a file that is not there yet; a file newer than the
// record (the app closed before the record was written) is adopted, a file
// older than the record is not trusted.
//
// VERSIONS. Version is written into both. A record or file of another
// version reads as no record (version 1, the single-atlas format, is simply
// dropped: the next new best fills the slot again); never throws on a corrupt
// or missing file. The slot is per device: it is not part of the cloud save
// (too big for it).
public static class KillCamArchive
{
    public const int Version = 2;
    public const string PrefsKey = "killcamBest";
    public const string FileName = "killcam_best.kcam";
    public const string CodexId = "log_killcam";
    public const float StoreGap = 1f / 40f;
    // Texels a world unit on screen: the viewer draws the run's ~13 u tall view
    // into at most CodexReplayViewer.MaxRenderEdge (1920) pixels, ~150 a unit.
    public const float Density = 150f;
    public const float LargeDensity = 96f, LargeExtent = 6f;
    public const int PageSize = 2048, MaxPages = 4, Pad = 2;
    public const int MeshTextureMax = 2048;
    public const string DownsampleShader = "KillCamDownsample";
    const string Magic = "KCAM";
    const float PosQ = 256f, ZQ = 64f, SizeQ = 256f;

    // Tests: where the file goes (null: persistentDataPath).
    public static string DirectoryOverride;
    public static string FilePath
    {
        get { return Path.Combine(DirectoryOverride ?? Application.persistentDataPath, FileName); }
    }

    public sealed class Record
    {
        public long score;
        public long utcTicks;
        public int ship = -1;
        public string world = "";
        public string killer = "";

        public string Format()
        {
            return Version + "|" + score + "|" + utcTicks + "|" + ship + "|" + Clean(world) + "|" + Clean(killer);
        }

        public static Record Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var parts = text.Split('|');
            if (parts.Length < 6 || parts[0] != Version.ToString()) return null;
            var r = new Record();
            if (!long.TryParse(parts[1], out r.score) || !long.TryParse(parts[2], out r.utcTicks) || !int.TryParse(parts[3], out r.ship)) return null;
            r.world = parts[4];
            r.killer = parts[5];
            return r;
        }

        public bool SameAs(Record o)
        {
            return o != null && o.score == score && o.utcTicks == utcTicks && o.ship == ship && o.world == world && o.killer == killer;
        }

        static string Clean(string s) { return string.IsNullOrEmpty(s) ? "" : s.Replace("|", "/"); }
    }

    // ---- the policy ----

    // A run's death is kept when it set a new best on a ranked run.
    public static bool ShouldSave(bool newBest, bool ranked) { return newBest && ranked; }

    public static Record Latest
    {
        get
        {
            Settle();
            return Record.Parse(PlayerPrefs.GetString(PrefsKey, ""));
        }
    }
    public static bool HasRecord { get { return Latest != null && File.Exists(FilePath); } }

    // How the codex describes the slot.
    public static string Describe()
    {
        var r = Latest;
        if (r == null || !File.Exists(FilePath))
            return "No last stand on record yet.\n\nSet a new best score and the run's final moments are kept here as a slow-motion replay: " +
                   "only the latest one, each new best replaces it.";
        var sb = new StringBuilder(160);
        sb.Append("Your latest new best, and how it ended.\n\n");
        sb.Append("SCORE  ").Append(r.score.ToString("N0")).Append('\n');
        if (!string.IsNullOrEmpty(r.world)) sb.Append("WORLD  ").Append(r.world.ToUpperInvariant()).Append('\n');
        string ship = ShipId.NameOf(r.ship);
        if (!string.IsNullOrEmpty(ship)) sb.Append("SHIP  ").Append(ship.ToUpperInvariant()).Append('\n');
        string killer = DamageLog.NameOf(r.killer);
        if (!string.IsNullOrEmpty(killer)) sb.Append("KILLED BY  ").Append(killer.ToUpperInvariant()).Append('\n');
        if (r.utcTicks > 0) sb.Append("FLOWN  ").Append(new DateTime(r.utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("d MMM yyyy"));
        return sb.ToString().TrimEnd('\n');
    }

    // The run that just ended (playerIsDead.BeginResults).
    public static Record RecordForThisRun(long score)
    {
        var r = new Record { score = score, utcTicks = DateTime.UtcNow.Ticks, ship = ShipId.Equipped(), killer = DamageLog.RunKiller ?? "" };
        try
        {
            var theme = WorldManager.InSpaceLeg ? null : WorldManager.Current;
            r.world = WorldManager.InSpaceLeg ? "Space" : theme != null ? theme.displayName : "";
        }
        catch (Exception) { r.world = ""; }
        return r;
    }

    // ---- saving ----

    // The write in flight (its result: the record it wrote, null if it failed).
    public static Task<Record> Pending { get; private set; }
    // Read-outs (tests, the log): the last save's packing.
    public static int LastPages { get; private set; }
    public static long LastPackMs { get; private set; }
    public static float LastDensityScale { get; private set; }

    // Keeps `clip` (the live tape's last seconds) as the latest high-score
    // death. The GPU copy happens now; encoding and writing in the background
    // (`background` false: all now -- tests). False when nothing could be kept.
    public static bool Save(KillCamClip clip, Record meta, bool background = true)
    {
        if (clip == null || !clip.Playable || meta == null) return false;
        Packed packed;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try { packed = Pack(clip, meta); }
        catch (Exception ex) { Debug.LogWarning("[KillCam] not saved: " + ex.Message); return false; }
        LastPackMs = watch.ElapsedMilliseconds;
        if (packed == null) return false;
        string path = FilePath;
        if (!background)
        {
            if (WriteFile(packed, path) != null) Commit(meta);
            return true;
        }
        // one write at a time, in order: the latest new best is the one that stays
        var before = Pending;
        Pending = before == null || before.IsCompleted
            ? Task.Run(() => WriteFile(packed, path))
            : before.ContinueWith(_ => WriteFile(packed, path), TaskScheduler.Default);
        return true;
    }

    // Main thread: a finished write's record goes into PlayerPrefs (Latest calls this).
    public static void Settle()
    {
        var t = Pending;
        if (t == null || !t.IsCompleted) return;
        Pending = null;
        if (t.Status == TaskStatus.RanToCompletion && t.Result != null) Commit(t.Result);
    }

    static void Commit(Record meta)
    {
        PlayerPrefs.SetString(PrefsKey, meta.Format());
        PrefsSaver.MarkDirty();
    }

    static Record WriteFile(Packed packed, string path)
    {
        try
        {
            byte[] bytes = Serialize(packed);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); }
                catch (Exception) { File.Delete(path); File.Move(tmp, path); }
            }
            else File.Move(tmp, path);
            return packed.meta;
        }
        catch (Exception ex) { Debug.LogWarning("[KillCam] write failed: " + ex.Message); return null; }
    }

    // Forgets the slot (tests, a progress reset).
    public static void Clear()
    {
        var t = Pending;
        if (t != null) { try { t.Wait(); } catch (Exception) { } }
        Pending = null;
        PlayerPrefs.DeleteKey(PrefsKey);
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch (Exception) { }
    }

    // ---- the packed form (pure data, so it serialises off the main thread) ----

    public sealed class MatSpec
    {
        public string shader;
        public readonly List<string> names = new List<string>();
        public readonly List<byte> kinds = new List<byte>();      // 0 float, 1 colour, 2 vector
        public readonly List<Vector4> values = new List<Vector4>();
    }

    public sealed class MeshSpec
    {
        public Vector3 center;
        public Vector2 size, scale;
        public int layer, material = -1;
        public short order;
        public int width, height;
        public byte[] rgba;
    }

    public struct PEntry
    {
        public ushort track, sprite;
        public short material, order;
        public int layer;
        public byte flags;
        public Vector3 pos;              // relative to the frame's camera
        public float m00, m01, m10, m11;
        public Color32 color;
        public Vector2 size;
    }

    public struct PMesh
    {
        public byte track;
        public Vector2 offset;
    }

    public sealed class Page
    {
        public int width, height;
        public byte[] rgba;              // raw RGBA32, bottom row first (packing side)
        public byte[] png;               // encoded (loading side)
    }

    public sealed class Packed
    {
        public Record meta = new Record();
        public float camHalfHeight, camAspect, camZ;
        public Color32 background;
        public float impact = -1f;       // the fatal hit on the frames' clock (-1: the last frame)
        public int frames;
        public float[] time;
        public Vector2[] cam;
        public int[] counts, meshCounts;
        public readonly List<PEntry> entries = new List<PEntry>();
        public readonly List<PMesh> meshEntries = new List<PMesh>();
        // sprites: page, rects (pixels), pivots (0..1), pixels per unit, borders (pixels)
        public readonly List<byte> pages = new List<byte>();
        public readonly List<RectInt> rects = new List<RectInt>();
        public readonly List<Vector2> pivots = new List<Vector2>();
        public readonly List<float> ppu = new List<float>();
        public readonly List<Vector4> borders = new List<Vector4>();
        public readonly List<Page> atlas = new List<Page>();
        public readonly List<MatSpec> materials = new List<MatSpec>();
        public readonly List<MeshSpec> meshes = new List<MeshSpec>();
    }

    // ---- packing (main thread: reads the live sprites through the GPU) ----

    // Shaders a sprite is drawn through into the atlas instead of re-made on
    // it. Their look is worked out in the sprite's own texture space: the
    // sky's cross-fade over v 0..1 (BackdropSkyWrap), a planet's disc in its
    // texture's uv (BackdropPlanet), the planetfall's uv scroll, a plate feather
    // in 256 px cells (BackdropGrade), a lamp hash per texel (BackdropBlink) --
    // re-made on a sprite cut into the clip's own atlas they would sample its
    // neighbours (a planet wrapped in scraps of other sprites); or it lives in
    // the renderer's property block, which a saved clip does not carry: the
    // exhaust skins' palette shift (ExhaustRemap). So each such sprite is drawn
    // through its material -- its renderer's settings too, as at the moment of
    // death -- into the atlas (a slot per sprite and material) and replayed
    // with the plain sprite material.
    public static readonly string[] BakedShaders =
    {
        "Pause/BackdropSkyWrap", "Pause/BackdropPlanet", "Pause/PlanetfallLayer", "Pause/BackdropGrade", "Pause/BackdropBlink",
        ExhaustRemap.ShaderName,
    };

    public static bool Bakes(Material m)
    {
        return m != null && m.shader != null && Array.IndexOf(BakedShaders, m.shader.name) >= 0;
    }

    public sealed class Slot
    {
        public Sprite sprite;
        public Material bake;            // drawn through this (BakedShaders), else copied as is
        public SpriteRenderer source;    // the live renderer: its property block goes into the bake
        public Texture texture;
        public Rect uv;
        public Vector2 worldSize, pivot;
        public float srcW, srcH, srcPpu;
        public float scaleX, scaleY;     // the largest scale an entry draws it at (its on-screen size: worldSize x these)
        public Vector4 border;
        public int w, h, x, y, page;
    }

    static Packed Pack(KillCamClip clip, Record meta)
    {
        var p = new Packed { meta = meta, camHalfHeight = clip.camHalfHeight, camAspect = clip.camAspect, camZ = clip.camZ, background = clip.background };
        // the frames to keep: StoreGap apart, always the hit and the last
        float hit = clip.impact >= 0f ? clip.ImpactTime : -1f;
        var keep = new List<int>();
        float lastKept = float.NegativeInfinity;
        for (int f = 0; f < clip.frameCount; f++)
        {
            bool must = f == clip.frameCount - 1 || (hit >= 0f && Mathf.Abs(clip.time[f] - hit) < 1e-4f);
            if (!must && clip.time[f] - lastKept < StoreGap * .999f) continue;
            keep.Add(f);
            lastKept = clip.time[f];
        }
        p.impact = hit;
        p.frames = keep.Count;
        p.time = new float[p.frames];
        p.cam = new Vector2[p.frames];
        p.counts = new int[p.frames];
        p.meshCounts = new int[p.frames];

        var slots = new List<Slot>();
        var spriteIndex = new Dictionary<(Sprite, Material), int>();
        var matIndex = new Dictionary<Material, int>();
        var baked = new Dictionary<Material, bool>();
        var trackIndex = new Dictionary<int, int>();
        var meshIndex = new Dictionary<int, int>();
        var meshRenderers = new List<Renderer>();

        for (int k = 0; k < keep.Count; k++)
        {
            int f = keep[k];
            p.time[k] = clip.time[f];
            p.cam[k] = clip.cam[f];
            int n = 0;
            for (int i = 0; i < clip.count[f]; i++)
            {
                var e = clip.Entry(f, i);
                bool bakes = false;   // (shader.name allocates: once per material)
                if (e.material != null && !baked.TryGetValue(e.material, out bakes)) baked[e.material] = bakes = Bakes(e.material);
                var bake = bakes ? e.material : null;
                int s = SlotOf(e.sprite, bake, e.source, slots, spriteIndex);
                if (s < 0) continue;
                Grow(slots[s], e);
                int track;
                if (!trackIndex.TryGetValue(e.track, out track))
                {
                    if (trackIndex.Count >= ushort.MaxValue) continue;
                    track = trackIndex.Count;
                    trackIndex[e.track] = track;
                }
                p.entries.Add(new PEntry
                {
                    track = (ushort)track, sprite = (ushort)s, material = (short)(bake != null ? -1 : MaterialOf(e.material, p, matIndex)),
                    order = e.order, layer = e.layer, flags = e.flags,
                    pos = new Vector3(e.pos.x - clip.cam[f].x, e.pos.y - clip.cam[f].y, e.pos.z),
                    m00 = e.m00, m01 = e.m01, m10 = e.m10, m11 = e.m11, color = e.color, size = e.size,
                });
                n++;
            }
            p.counts[k] = n;
            int mn = 0;
            for (int i = 0; i < clip.meshCount[f]; i++)
            {
                var me = clip.MeshEntry(f, i);
                int mi;
                if (!meshIndex.TryGetValue(me.track, out mi))
                {
                    if (me.renderer == null || meshIndex.Count >= 32) continue;
                    mi = meshIndex.Count;
                    meshIndex[me.track] = mi;
                    meshRenderers.Add(me.renderer);
                }
                p.meshEntries.Add(new PMesh { track = (byte)mi, offset = me.offset });
                mn++;
            }
            p.meshCounts[k] = mn;
        }
        if (p.entries.Count == 0) return null;

        if (!PackAtlas(slots, p)) return null;
        foreach (var r in meshRenderers) p.meshes.Add(MeshOf(r, p, matIndex));
        return p;
    }

    // An entry draws its slot at its world matrix's column lengths (a tiled or
    // sliced sprite at its size: the tile itself still at the matrix's scale).
    public static void Grow(Slot s, in KillCamEntry e)
    {
        float sx = Mathf.Sqrt(e.m00 * e.m00 + e.m10 * e.m10), sy = Mathf.Sqrt(e.m01 * e.m01 + e.m11 * e.m11);
        if (sx > s.scaleX) s.scaleX = sx;
        if (sy > s.scaleY) s.scaleY = sy;
    }

    static int SlotOf(Sprite sprite, Material bake, SpriteRenderer source, List<Slot> slots, Dictionary<(Sprite, Material), int> index)
    {
        if (sprite == null) return -1;
        int i;
        if (index.TryGetValue((sprite, bake), out i)) return i;
        i = -1;
        var tex = sprite.texture;
        if (tex != null && slots.Count < ushort.MaxValue)
        {
            Rect r;
            Vector2 offset;
            try { r = sprite.textureRect; offset = sprite.textureRectOffset; }
            catch (Exception) { r = sprite.rect; offset = Vector2.zero; }
            float ppu = Mathf.Max(.0001f, sprite.pixelsPerUnit);
            if (r.width >= 1f && r.height >= 1f)
            {
                var slot = new Slot
                {
                    sprite = sprite, bake = bake, source = source, texture = tex,
                    uv = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height),
                    worldSize = r.size / ppu, srcW = r.width, srcH = r.height, srcPpu = ppu,
                    pivot = new Vector2((sprite.pivot.x - offset.x) / r.width, (sprite.pivot.y - offset.y) / r.height),
                    border = sprite.border,
                };
                i = slots.Count;
                slots.Add(slot);
            }
        }
        index[(sprite, bake)] = i;
        return i;
    }

    static int MaterialOf(Material m, Packed p, Dictionary<Material, int> index)
    {
        if (m == null || m.shader == null) return -1;
        int i;
        if (index.TryGetValue(m, out i)) return i;
        i = -1;
        string name = m.shader.name;
        if (name != "Sprites/Default")
        {
            var spec = new MatSpec { shader = name };
            var sh = m.shader;
            int n = sh.GetPropertyCount();
            for (int k = 0; k < n; k++)
            {
                string prop = sh.GetPropertyName(k);
                switch (sh.GetPropertyType(k))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        spec.names.Add(prop); spec.kinds.Add(0); spec.values.Add(new Vector4(m.GetFloat(prop), 0f, 0f, 0f)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        spec.names.Add(prop); spec.kinds.Add(1); spec.values.Add(m.GetColor(prop)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        spec.names.Add(prop); spec.kinds.Add(2); spec.values.Add(m.GetVector(prop)); break;
                }
            }
            i = p.materials.Count;
            p.materials.Add(spec);
        }
        index[m] = i;
        return i;
    }

    static MeshSpec MeshOf(Renderer r, Packed p, Dictionary<Material, int> matIndex)
    {
        var spec = new MeshSpec();
        var b = r.bounds;
        spec.center = b.center;
        spec.size = new Vector2(b.size.x, b.size.y);
        spec.layer = r.sortingLayerID;
        spec.order = (short)Mathf.Clamp(r.sortingOrder, short.MinValue, short.MaxValue);
        var mat = r.sharedMaterial;
        spec.material = MaterialOf(mat, p, matIndex);
        spec.scale = mat != null ? mat.mainTextureScale : Vector2.one;
        var tex = mat != null ? mat.mainTexture : null;
        if (tex != null)
        {
            float k = Mathf.Min(1f, MeshTextureMax / (float)Mathf.Max(tex.width, tex.height));
            spec.width = Mathf.Max(1, Mathf.RoundToInt(tex.width * k));
            spec.height = Mathf.Max(1, Mathf.RoundToInt(tex.height * k));
            var rt = RenderTexture.GetTemporary(spec.width, spec.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            Graphics.Blit(tex, rt);
            spec.rgba = ReadBack(rt, spec.width, spec.height, false);
            RenderTexture.ReleaseTemporary(rt);
        }
        return spec;
    }

    // The texels a slot gets at density scale `k`: its on-screen size x the density, at most what its source has.
    public static void SizeSlot(Slot s, float k)
    {
        float sx = s.worldSize.x * Mathf.Max(s.scaleX, 1e-3f), sy = s.worldSize.y * Mathf.Max(s.scaleY, 1e-3f);
        float density = (Mathf.Max(sx, sy) > LargeExtent ? LargeDensity : Density) * k;
        int most = PageSize - 2 * Pad - 4;
        s.w = Mathf.Clamp(Mathf.CeilToInt(sx * density), 1, Mathf.Min(most, Mathf.CeilToInt(s.srcW)));
        s.h = Mathf.Clamp(Mathf.CeilToInt(sy * density), 1, Mathf.Min(most, Mathf.CeilToInt(s.srcH)));
    }

    // Shelf-packs the slots into pages (shrinking the density a step at a time
    // until they fit MaxPages) and copies them in.
    public static bool PackAtlas(List<Slot> slots, Packed p)
    {
        if (slots.Count == 0) return false;
        float k = 1f;
        for (int attempt = 0; attempt < 14; attempt++, k *= .85f)
        {
            long area = 16;
            foreach (var s in slots)
            {
                SizeSlot(s, k);
                area += (long)(s.w + 2 * Pad) * (s.h + 2 * Pad);
            }
            // the smallest page edge the whole lot might fit on (one page), else full pages
            int width = area * 5 / 4 <= 512L * 512L ? 512 : area * 5 / 4 <= 1024L * 1024L ? 1024 : PageSize;
            int pages;
            List<int> heights;
            bool ok = Shelf(slots, width, out pages, out heights) && (pages == 1 || width == PageSize);
            if (!ok && width < PageSize)
            {
                width = PageSize;
                ok = Shelf(slots, width, out pages, out heights);
            }
            if (!ok || pages > MaxPages) continue;
            for (int page = 0; page < pages; page++)
            {
                var rgba = DrawPage(slots, page, width, heights[page]);
                if (rgba == null) return false;
                p.atlas.Add(new Page { width = width, height = heights[page], rgba = rgba });
            }
            foreach (var s in slots)
            {
                float scale = s.w / s.srcW;
                p.pages.Add((byte)s.page);
                p.rects.Add(new RectInt(s.x, s.y, s.w, s.h));
                p.pivots.Add(s.pivot);
                p.ppu.Add(s.srcPpu * scale);
                p.borders.Add(s.border * scale);
            }
            LastPages = pages;
            LastDensityScale = k;
            return true;
        }
        return false;
    }

    // Shelves, tallest first, on pages `width` wide and tall: a slot takes its
    // w x h plus Pad all round. False when a slot cannot fit a page at all.
    public static bool Shelf(List<Slot> slots, int width, out int pages, out List<int> heights)
    {
        var order = new List<Slot>(slots);
        order.Sort((a, b) => b.h.CompareTo(a.h));
        heights = new List<int>();
        pages = 0;
        int x = 4, y = 0, shelf = 0, page = 0;   // a page's first 4x4 holds the orientation marker
        foreach (var s in order)
        {
            int cw = s.w + 2 * Pad, ch = s.h + 2 * Pad;
            if (cw + 4 > width || ch > width) return false;
            if (x + cw > width) { y += shelf; x = 0; shelf = 0; }
            if (y + ch > width)
            {
                heights.Add(Mathf.Max(4, (y + 3) & ~3));
                page++;
                x = 4; y = 0; shelf = 0;
            }
            s.page = page;
            s.x = x + Pad;
            s.y = y + Pad;
            x += cw;
            shelf = Mathf.Max(shelf, ch);
        }
        heights.Add(Mathf.Max(4, (y + shelf + 3) & ~3));
        pages = page + 1;
        return true;
    }

    static Material copyMaterial, downsampleMaterial;
    static readonly int StepId = Shader.PropertyToID("_Step"), RectId = Shader.PropertyToID("_Rect");

    // One page: a pass of textured quads into a render texture, read back once.
    // A white marker in the bottom-left corner tells which way up the read-back
    // came. A shrinking copy goes through the box filter; a baked slot
    // (BakedShaders) through a copy of its material (those write premultiplied
    // colour, put back to straight alpha after). Then every slot's edges are
    // extruded into its gutter.
    static byte[] DrawPage(List<Slot> slots, int page, int w, int h)
    {
        if (copyMaterial == null)
        {
            var shader = Shader.Find("Hidden/BlitCopy");
            if (shader == null) { Debug.LogWarning("[KillCam] no copy shader"); return null; }
            copyMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        if (downsampleMaterial == null)
        {
            var shader = Resources.Load<Shader>(DownsampleShader);
            if (shader != null) downsampleMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        var previous = RenderTexture.active;
        var made = new List<Material>();
        RenderTexture.active = rt;
        GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
        GL.PushMatrix();
        GL.LoadPixelMatrix(0f, w, 0f, h);
        Quad(copyMaterial, Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Rect(0f, 0f, 2f, 2f));
        foreach (var s in slots)
        {
            if (s.page != page) continue;
            var m = copyMaterial;
            if (s.bake != null) { m = BakeMaterial(s); made.Add(m); }
            else if (downsampleMaterial != null && (s.srcW > s.w * 1.01f || s.srcH > s.h * 1.01f))
            {
                m = downsampleMaterial;
                m.SetVector(StepId, new Vector4(s.uv.width / s.w, s.uv.height / s.h, 0f, 0f));
                m.SetVector(RectId, new Vector4(s.uv.xMin, s.uv.yMin, s.uv.xMax, s.uv.yMax));
            }
            Quad(m, s.texture, s.uv, new Rect(s.x, s.y, s.w, s.h));
        }
        GL.PopMatrix();
        RenderTexture.active = previous;
        var bytes = ReadBack(rt, w, h, true);
        RenderTexture.ReleaseTemporary(rt);
        foreach (var m in made) UnityEngine.Object.DestroyImmediate(m);
        foreach (var s in slots)
        {
            if (s.page != page) continue;
            var r = new RectInt(s.x, s.y, s.w, s.h);
            if (s.bake != null) Unpremultiply(bytes, w, r);
            Extrude(bytes, w, h, r, Pad);
        }
        return bytes;
    }

    // Copies a rect's edge texels `pad` texels outward (its corners too), so
    // bilinear sampling at the sprite's edge reads the sprite, not the gutter.
    public static void Extrude(byte[] px, int w, int h, RectInt r, int pad)
    {
        if (r.width <= 0 || r.height <= 0) return;
        int x0 = Mathf.Max(0, r.xMin - pad), x1 = Mathf.Min(w - 1, r.xMax - 1 + pad);
        int y0 = Mathf.Max(0, r.yMin - pad), y1 = Mathf.Min(h - 1, r.yMax - 1 + pad);
        // rows of the rect: left and right
        for (int y = r.yMin; y < r.yMax; y++)
        {
            int row = y * w;
            int left = (row + r.xMin) * 4, right = (row + r.xMax - 1) * 4;
            for (int x = x0; x < r.xMin; x++) Buffer.BlockCopy(px, left, px, (row + x) * 4, 4);
            for (int x = r.xMax; x <= x1; x++) Buffer.BlockCopy(px, right, px, (row + x) * 4, 4);
        }
        // whole rows (with their extruded ends) below and above
        int span = (x1 - x0 + 1) * 4;
        for (int y = y0; y < r.yMin; y++) Buffer.BlockCopy(px, (r.yMin * w + x0) * 4, px, (y * w + x0) * 4, span);
        for (int y = r.yMax; y <= y1; y++) Buffer.BlockCopy(px, ((r.yMax - 1) * w + x0) * 4, px, (y * w + x0) * 4, span);
    }

    static MaterialPropertyBlock bakeBlock;

    // The slot's material as its renderer drew it: the material's own settings
    // and, over them, the renderer's property block (a planet's disc and turn,
    // an exhaust skin's palette shift).
    static Material BakeMaterial(Slot s)
    {
        var m = new Material(s.bake) { hideFlags = HideFlags.HideAndDontSave };
        if (s.source == null || !s.source.HasPropertyBlock()) return m;
        if (bakeBlock == null) bakeBlock = new MaterialPropertyBlock();
        s.source.GetPropertyBlock(bakeBlock);
        var sh = m.shader;
        int n = sh.GetPropertyCount();
        for (int k = 0; k < n; k++)
        {
            string prop = sh.GetPropertyName(k);
            switch (sh.GetPropertyType(k))
            {
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    if (bakeBlock.HasFloat(prop)) m.SetFloat(prop, bakeBlock.GetFloat(prop)); break;
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    if (bakeBlock.HasColor(prop)) m.SetColor(prop, bakeBlock.GetColor(prop)); break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    if (bakeBlock.HasVector(prop)) m.SetVector(prop, bakeBlock.GetVector(prop)); break;
            }
        }
        // properties set on the block that the shader does not list (the remap's hidden bands)
        foreach (var name in BlockVectors)
            if (bakeBlock.HasVector(name)) m.SetVector(name, bakeBlock.GetVector(name));
        return m;
    }

    // The exhaust remap's per-renderer bands (not in its Properties block).
    static readonly string[] BlockVectors =
    {
        "_ExSrc0", "_ExSrc1", "_ExSrc2", "_ExSrc3", "_ExSrc4", "_ExShift0", "_ExShift1", "_ExShift2", "_ExShift3", "_ExShift4",
    };

    static void Unpremultiply(byte[] px, int w, RectInt r)
    {
        for (int y = r.yMin; y < r.yMax; y++)
            for (int x = r.xMin; x < r.xMax; x++)
            {
                int at = (y * w + x) * 4;
                int a = px[at + 3];
                if (a == 0 || a == 255) continue;
                for (int c = 0; c < 3; c++) px[at + c] = (byte)Mathf.Min(255, (px[at + c] * 255 + a / 2) / a);
            }
    }

    static void Quad(Material m, Texture t, Rect uv, Rect d)
    {
        m.mainTexture = t;
        m.SetPass(0);
        GL.Begin(GL.QUADS);
        GL.Color(Color.white);
        GL.TexCoord2(uv.xMin, uv.yMin); GL.Vertex3(d.xMin, d.yMin, 0f);
        GL.TexCoord2(uv.xMin, uv.yMax); GL.Vertex3(d.xMin, d.yMax, 0f);
        GL.TexCoord2(uv.xMax, uv.yMax); GL.Vertex3(d.xMax, d.yMax, 0f);
        GL.TexCoord2(uv.xMax, uv.yMin); GL.Vertex3(d.xMax, d.yMin, 0f);
        GL.End();
    }

    // The render texture's pixels, RGBA32, bottom row first. With `marker`, the
    // 2x2 white corner is checked (and cleared): found at the top, the rows flip.
    static byte[] ReadBack(RenderTexture rt, int w, int h, bool marker)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        RenderTexture.active = previous;
        var bytes = tex.GetRawTextureData();
        UnityEngine.Object.DestroyImmediate(tex);
        if (marker && h > 2)
        {
            bool bottom = bytes[3] > 200;
            bool top = bytes[((h - 1) * w) * 4 + 3] > 200;
            if (!bottom && top) FlipRows(bytes, w, h);
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                {
                    int at = (y * w + x) * 4;
                    bytes[at] = bytes[at + 1] = bytes[at + 2] = bytes[at + 3] = 0;
                }
        }
        return bytes;
    }

    static void FlipRows(byte[] px, int w, int h)
    {
        int row = w * 4;
        var tmp = new byte[row];
        for (int y = 0; y < h / 2; y++)
        {
            Buffer.BlockCopy(px, y * row, tmp, 0, row);
            Buffer.BlockCopy(px, (h - 1 - y) * row, px, y * row, row);
            Buffer.BlockCopy(tmp, 0, px, (h - 1 - y) * row, row);
        }
    }

    // ---- the file (any thread) ----

    public static byte[] Serialize(Packed p)
    {
        var payload = new MemoryStream();
        using (var w = new BinaryWriter(payload, Encoding.UTF8, true))
        {
            w.Write(p.meta.Format());
            w.Write(p.camHalfHeight); w.Write(p.camAspect); w.Write(p.camZ);
            w.Write(p.background.r); w.Write(p.background.g); w.Write(p.background.b); w.Write(p.background.a);
            w.Write(p.impact);

            w.Write(p.atlas.Count);
            foreach (var page in p.atlas)
            {
                byte[] png = page.png ?? Png(page.rgba, page.width, page.height);
                w.Write(page.width); w.Write(page.height);
                w.Write(png.Length); w.Write(png);
            }
            w.Write(p.rects.Count);
            for (int i = 0; i < p.rects.Count; i++)
            {
                var r = p.rects[i];
                w.Write(i < p.pages.Count ? p.pages[i] : (byte)0);
                w.Write((ushort)r.x); w.Write((ushort)r.y); w.Write((ushort)r.width); w.Write((ushort)r.height);
                w.Write(p.pivots[i].x); w.Write(p.pivots[i].y);
                w.Write(p.ppu[i]);
                var b = p.borders[i];
                w.Write(b.x); w.Write(b.y); w.Write(b.z); w.Write(b.w);
            }

            w.Write(p.materials.Count);
            foreach (var m in p.materials)
            {
                w.Write(m.shader ?? "");
                w.Write(m.names.Count);
                for (int i = 0; i < m.names.Count; i++)
                {
                    w.Write(m.names[i]); w.Write(m.kinds[i]);
                    var v = m.values[i];
                    w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w);
                }
            }

            w.Write(p.meshes.Count);
            foreach (var m in p.meshes)
            {
                w.Write(m.center.x); w.Write(m.center.y); w.Write(m.center.z);
                w.Write(m.size.x); w.Write(m.size.y); w.Write(m.scale.x); w.Write(m.scale.y);
                w.Write(m.layer); w.Write(m.order); w.Write(m.material);
                byte[] mpng = m.rgba != null ? Png(m.rgba, m.width, m.height) : new byte[0];
                w.Write(mpng.Length); w.Write(mpng);
            }

            w.Write(p.frames);
            int e = 0, me = 0;
            for (int f = 0; f < p.frames; f++)
            {
                w.Write(p.time[f]); w.Write(p.cam[f].x); w.Write(p.cam[f].y);
                w.Write((ushort)p.counts[f]);
                for (int i = 0; i < p.counts[f]; i++, e++)
                {
                    var x = p.entries[e];
                    w.Write(x.track); w.Write(x.sprite); w.Write(x.material); w.Write(x.layer); w.Write(x.order); w.Write(x.flags);
                    w.Write(Q(x.pos.x, PosQ)); w.Write(Q(x.pos.y, PosQ)); w.Write(Q(x.pos.z, ZQ));
                    // the matrix as floats: a long stretched sprite (a beam, a full-height lane) keeps its length
                    w.Write(x.m00); w.Write(x.m01); w.Write(x.m10); w.Write(x.m11);
                    w.Write(x.color.r); w.Write(x.color.g); w.Write(x.color.b); w.Write(x.color.a);
                    if (((x.flags >> KillCamEntry.DrawModeShift) & 3) != 0) { w.Write(Q(x.size.x, SizeQ)); w.Write(Q(x.size.y, SizeQ)); }
                }
                w.Write((byte)p.meshCounts[f]);
                for (int i = 0; i < p.meshCounts[f]; i++, me++)
                {
                    var m = p.meshEntries[me];
                    w.Write(m.track); w.Write(m.offset.x); w.Write(m.offset.y);
                }
            }
        }

        var file = new MemoryStream();
        file.Write(Encoding.ASCII.GetBytes(Magic), 0, 4);
        file.Write(BitConverter.GetBytes(Version), 0, 4);
        using (var gz = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal, true))
        {
            payload.Position = 0;
            payload.CopyTo(gz);
        }
        return file.ToArray();
    }

    static byte[] Png(byte[] rgba, int w, int h)
    {
        if (rgba == null || w <= 0 || h <= 0) return new byte[0];
        return ImageConversion.EncodeArrayToPNG(rgba, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h);
    }

    static short Q(float v, float scale)
    {
        return (short)Mathf.Clamp(Mathf.RoundToInt(v * scale), short.MinValue, short.MaxValue);
    }

    // Null for anything that is not a whole file of this version.
    public static Packed Deserialize(byte[] bytes)
    {
        try
        {
            if (bytes == null || bytes.Length < 8 || Encoding.ASCII.GetString(bytes, 0, 4) != Magic) return null;
            if (BitConverter.ToInt32(bytes, 4) != Version) return null;
            var payload = new MemoryStream();
            using (var gz = new GZipStream(new MemoryStream(bytes, 8, bytes.Length - 8), CompressionMode.Decompress))
                gz.CopyTo(payload);
            payload.Position = 0;
            var p = new Packed();
            using (var r = new BinaryReader(payload))
            {
                p.meta = Record.Parse(r.ReadString());
                if (p.meta == null) return null;
                p.camHalfHeight = r.ReadSingle(); p.camAspect = r.ReadSingle(); p.camZ = r.ReadSingle();
                p.background = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                p.impact = r.ReadSingle();

                int pages = r.ReadInt32();
                if (pages < 1 || pages > MaxPages) return null;
                for (int i = 0; i < pages; i++)
                {
                    var page = new Page { width = r.ReadInt32(), height = r.ReadInt32() };
                    page.png = r.ReadBytes(r.ReadInt32());
                    p.atlas.Add(page);
                }
                int sprites = r.ReadInt32();
                for (int i = 0; i < sprites; i++)
                {
                    p.pages.Add(r.ReadByte());
                    p.rects.Add(new RectInt(r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16()));
                    p.pivots.Add(new Vector2(r.ReadSingle(), r.ReadSingle()));
                    p.ppu.Add(r.ReadSingle());
                    p.borders.Add(new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                }

                int mats = r.ReadInt32();
                for (int i = 0; i < mats; i++)
                {
                    var m = new MatSpec { shader = r.ReadString() };
                    int n = r.ReadInt32();
                    for (int k = 0; k < n; k++)
                    {
                        m.names.Add(r.ReadString());
                        m.kinds.Add(r.ReadByte());
                        m.values.Add(new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                    }
                    p.materials.Add(m);
                }

                int meshes = r.ReadInt32();
                for (int i = 0; i < meshes; i++)
                {
                    var m = new MeshSpec();
                    m.center = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    m.size = new Vector2(r.ReadSingle(), r.ReadSingle());
                    m.scale = new Vector2(r.ReadSingle(), r.ReadSingle());
                    m.layer = r.ReadInt32(); m.order = r.ReadInt16(); m.material = r.ReadInt32();
                    m.rgba = r.ReadBytes(r.ReadInt32());   // (PNG bytes on the loading side)
                    p.meshes.Add(m);
                }

                p.frames = r.ReadInt32();
                if (p.frames < 0 || p.frames > 100000) return null;
                p.time = new float[p.frames];
                p.cam = new Vector2[p.frames];
                p.counts = new int[p.frames];
                p.meshCounts = new int[p.frames];
                for (int f = 0; f < p.frames; f++)
                {
                    p.time[f] = r.ReadSingle();
                    p.cam[f] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    int n = r.ReadUInt16();
                    p.counts[f] = n;
                    for (int i = 0; i < n; i++)
                    {
                        var x = new PEntry();
                        x.track = r.ReadUInt16(); x.sprite = r.ReadUInt16(); x.material = r.ReadInt16();
                        x.layer = r.ReadInt32(); x.order = r.ReadInt16(); x.flags = r.ReadByte();
                        x.pos = new Vector3(r.ReadInt16() / PosQ, r.ReadInt16() / PosQ, r.ReadInt16() / ZQ);
                        x.m00 = r.ReadSingle(); x.m01 = r.ReadSingle(); x.m10 = r.ReadSingle(); x.m11 = r.ReadSingle();
                        x.color = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                        if (((x.flags >> KillCamEntry.DrawModeShift) & 3) != 0) x.size = new Vector2(r.ReadInt16() / SizeQ, r.ReadInt16() / SizeQ);
                        p.entries.Add(x);
                    }
                    int mn = r.ReadByte();
                    p.meshCounts[f] = mn;
                    for (int i = 0; i < mn; i++)
                        p.meshEntries.Add(new PMesh { track = r.ReadByte(), offset = new Vector2(r.ReadSingle(), r.ReadSingle()) });
                }
            }
            return p;
        }
        catch (Exception) { return null; }
    }

    // ---- loading (main thread) ----

    // A loaded clip and everything made for it; Dispose frees it.
    public sealed class Loaded : IDisposable
    {
        public KillCamClip clip;
        public Record meta;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        public void Own(UnityEngine.Object o) { if (o != null) owned.Add(o); }
        public void Dispose()
        {
            foreach (var o in owned)
            {
                if (o == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
            }
            owned.Clear();
            clip = null;
        }
    }

    // The saved clip, or null (no record and no file, a stale / corrupt /
    // other-version file). A whole file newer than the record is adopted (the
    // record's write never happened: the app closed first); one older than the
    // record is not trusted.
    public static Loaded Load()
    {
        var record = Latest;
        byte[] bytes;
        try { if (!File.Exists(FilePath)) return null; bytes = File.ReadAllBytes(FilePath); }
        catch (Exception) { return null; }
        var p = Deserialize(bytes);
        if (p == null) return null;
        if (!p.meta.SameAs(record))
        {
            if (record != null && p.meta.utcTicks <= record.utcTicks) return null;
            Commit(p.meta);
        }
        return Build(p);
    }

    public static Loaded Build(Packed p)
    {
        if (p == null || p.frames < 1 || p.atlas.Count == 0) return null;
        var loaded = new Loaded { meta = p.meta };
        var pages = new Texture2D[p.atlas.Count];
        for (int i = 0; i < pages.Length; i++)
        {
            var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "KillCamAtlas" + i, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            loaded.Own(atlas);
            if (p.atlas[i].png == null || !atlas.LoadImage(p.atlas[i].png, false)) { loaded.Dispose(); return null; }
            pages[i] = atlas;
        }

        var sprites = new Sprite[p.rects.Count];
        for (int i = 0; i < sprites.Length; i++)
        {
            var r = p.rects[i];
            int page = i < p.pages.Count ? p.pages[i] : 0;
            if (page >= pages.Length) continue;
            var atlas = pages[page];
            if (r.xMax > atlas.width || r.yMax > atlas.height || r.width < 1 || r.height < 1) continue;
            sprites[i] = Sprite.Create(atlas, new Rect(r.x, r.y, r.width, r.height), p.pivots[i], Mathf.Max(.0001f, p.ppu[i]),
                                       0, SpriteMeshType.FullRect, p.borders[i]);
            loaded.Own(sprites[i]);
        }

        var materials = new Material[p.materials.Count];
        for (int i = 0; i < materials.Length; i++)
        {
            var spec = p.materials[i];
            var shader = string.IsNullOrEmpty(spec.shader) ? null : Shader.Find(spec.shader);
            if (shader == null) continue;
            var m = new Material(shader);
            for (int k = 0; k < spec.names.Count; k++)
            {
                if (!m.HasProperty(spec.names[k])) continue;
                var v = spec.values[k];
                if (spec.kinds[k] == 0) m.SetFloat(spec.names[k], v.x);
                else if (spec.kinds[k] == 1) m.SetColor(spec.names[k], v);
                else m.SetVector(spec.names[k], v);
            }
            materials[i] = m;
            loaded.Own(m);
        }

        var clip = new KillCamClip(p.frames, Mathf.Max(1, p.entries.Count), Mathf.Max(1, p.meshEntries.Count));
        clip.frameCount = p.frames;
        clip.camHalfHeight = p.camHalfHeight > 0f ? p.camHalfHeight : clip.camHalfHeight;
        clip.camAspect = p.camAspect > 0f ? p.camAspect : clip.camAspect;
        clip.camZ = p.camZ;
        clip.background = p.background;
        int e = 0, me = 0;
        float t0 = p.frames > 0 ? p.time[0] : 0f;
        clip.impact = p.impact >= 0f ? Mathf.Max(0f, p.impact - t0) : -1f;
        for (int f = 0; f < p.frames; f++)
        {
            clip.time[f] = p.time[f] - t0;
            clip.cam[f] = p.cam[f];
            clip.first[f] = e;
            clip.count[f] = p.counts[f];
            for (int i = 0; i < p.counts[f]; i++, e++)
            {
                var x = p.entries[e];
                clip.entries[e] = new KillCamEntry
                {
                    track = x.track,
                    sprite = x.sprite < sprites.Length ? sprites[x.sprite] : null,
                    material = x.material >= 0 && x.material < materials.Length ? materials[x.material] : null,
                    pos = new Vector3(x.pos.x + p.cam[f].x, x.pos.y + p.cam[f].y, x.pos.z),
                    m00 = x.m00, m01 = x.m01, m10 = x.m10, m11 = x.m11,
                    color = x.color, layer = x.layer, order = x.order, flags = x.flags, size = x.size,
                };
            }
            clip.meshFirst[f] = me;
            clip.meshCount[f] = p.meshCounts[f];
            for (int i = 0; i < p.meshCounts[f]; i++, me++)
                clip.meshEntries[me] = new KillCamMeshEntry { track = p.meshEntries[me].track, offset = p.meshEntries[me].offset };
        }

        clip.meshTracks = new KillCamMeshTrack[p.meshes.Count];
        for (int i = 0; i < p.meshes.Count; i++)
        {
            var spec = p.meshes[i];
            if (spec.rgba == null || spec.rgba.Length == 0) continue;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "KillCamRail" + i, wrapMode = TextureWrapMode.Repeat };
            loaded.Own(tex);
            if (!tex.LoadImage(spec.rgba, false)) continue;
            Material mat;
            var src = spec.material >= 0 && spec.material < materials.Length ? materials[spec.material] : null;
            if (src != null) mat = new Material(src);
            else
            {
                var sh = Shader.Find("Sprites/Default");
                if (sh == null) continue;
                mat = new Material(sh);
            }
            loaded.Own(mat);
            mat.mainTexture = tex;
            mat.mainTextureScale = spec.scale;
            clip.meshTracks[i] = new KillCamMeshTrack
            {
                center = spec.center, size = spec.size, layer = spec.layer, order = spec.order,
                texture = tex, material = mat, textureScale = spec.scale,
            };
        }
        loaded.clip = clip;
        return loaded;
    }
}
