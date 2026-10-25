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
// LiveWindow seconds (ShouldSave). Practice, developer and tutorial runs never do.
//
// WHAT. The live tape refers to sprites and materials of the running scene,
// which the menu does not have, so a saved clip carries its own pictures:
//   * every distinct sprite in the clip is copied through the GPU (one pass
//     of quads into one atlas, one read-back) at a modest density
//     (SmallDensity / LargeDensity texels a world unit, less if the atlas
//     would overflow AtlasMax), the atlas stored as a PNG;
//   * a material other than the default sprite one is stored as its shader's
//     name and its float / colour / vector settings (re-made on load when
//     the shader is in the build -- the backdrops' grading, the rails');
//   * each rail (a scrolling quad) as its bounds, settings, a copy of its
//     texture and its offset per frame;
//   * frames thinned to StoreGap apart (the replay blends between them),
//     positions relative to the camera, quantised to shorts.
// Then gzip. A busy board comes to a few hundred KB of frames plus the atlas.
//
// FILE. Application.persistentDataPath/FileName: "KCAM", the format version,
// then the gzipped payload, whose head repeats the record. Written to a temp
// file then moved over the old one, off the main thread (the PNG encode and
// gzip run in a Task: only the GPU copy happens on the main thread, once,
// as the Flight Complete panel opens). PrefsKey holds the record ("1|score|
// utc ticks|ship|world|killer") so the codex can describe it without opening
// the file; the file is only trusted when its own record matches that.
//
// VERSIONS. Version is written into both. A save without PrefsKey (every save
// before this) has no record: nothing to migrate. A record or file of another
// version reads as no record (a future format bumps Version and reads the
// old one or drops it); never throws on a corrupt or missing file. The slot
// is per device: it is not part of the cloud save (too big for it).
public static class KillCamArchive
{
    public const int Version = 1;
    public const string PrefsKey = "killcamBest";
    public const string FileName = "killcam_best.kcam";
    public const string CodexId = "log_killcam";
    public const float StoreGap = 1f / 32f;
    public const int AtlasMax = 2048;
    public const float SmallDensity = 56f, LargeDensity = 32f, LargeExtent = 2.5f;
    public const int MeshTextureMax = 1024;
    const string Magic = "KCAM";
    const float PosQ = 256f, ZQ = 64f, MatQ = 2048f, SizeQ = 256f;

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

    public static Record Latest { get { return Record.Parse(PlayerPrefs.GetString(PrefsKey, "")); } }
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

    public static Task Pending { get; private set; }

    // Keeps `clip` (the live tape's last seconds) as the latest high-score
    // death. The GPU copy happens now; encoding and writing in the background
    // (`background` false: all now -- tests). False when nothing could be kept.
    public static bool Save(KillCamClip clip, Record meta, bool background = true)
    {
        if (clip == null || !clip.Playable || meta == null) return false;
        Packed packed;
        try { packed = Pack(clip, meta); }
        catch (Exception ex) { Debug.LogWarning("[KillCam] not saved: " + ex.Message); return false; }
        if (packed == null) return false;
        PlayerPrefs.SetString(PrefsKey, meta.Format());
        PrefsSaver.MarkDirty();
        string path = FilePath;
        if (!background) { WriteFile(packed, path); return true; }
        Pending = Task.Run(() => WriteFile(packed, path));
        return true;
    }

    static void WriteFile(Packed packed, string path)
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
        }
        catch (Exception ex) { Debug.LogWarning("[KillCam] write failed: " + ex.Message); }
    }

    // Forgets the slot (tests, a progress reset).
    public static void Clear()
    {
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

    public sealed class Packed
    {
        public Record meta = new Record();
        public float camHalfHeight, camAspect, camZ;
        public Color32 background;
        public int frames;
        public float[] time;
        public Vector2[] cam;
        public int[] counts, meshCounts;
        public readonly List<PEntry> entries = new List<PEntry>();
        public readonly List<PMesh> meshEntries = new List<PMesh>();
        // sprites: atlas rects (pixels), pivots (0..1), atlas pixels per unit, borders (atlas pixels)
        public readonly List<RectInt> rects = new List<RectInt>();
        public readonly List<Vector2> pivots = new List<Vector2>();
        public readonly List<float> ppu = new List<float>();
        public readonly List<Vector4> borders = new List<Vector4>();
        public int atlasW, atlasH;
        public byte[] atlasRgba;         // raw RGBA32, bottom row first (packing side)
        public byte[] atlasPng;          // encoded (loading side)
        public readonly List<MatSpec> materials = new List<MatSpec>();
        public readonly List<MeshSpec> meshes = new List<MeshSpec>();
    }

    // ---- packing (main thread: reads the live sprites through the GPU) ----

    sealed class Slot
    {
        public Sprite sprite;
        public Texture texture;
        public Rect uv;
        public Vector2 worldSize, pivot;
        public float srcW, srcH, srcPpu;
        public Vector4 border;
        public int w, h, x, y;
    }

    static Packed Pack(KillCamClip clip, Record meta)
    {
        var p = new Packed { meta = meta, camHalfHeight = clip.camHalfHeight, camAspect = clip.camAspect, camZ = clip.camZ, background = clip.background };
        // the frames to keep: StoreGap apart, always the last
        var keep = new List<int>();
        float lastKept = float.NegativeInfinity;
        for (int f = 0; f < clip.frameCount; f++)
        {
            bool last = f == clip.frameCount - 1;
            if (!last && clip.time[f] - lastKept < StoreGap * .999f) continue;
            keep.Add(f);
            lastKept = clip.time[f];
        }
        p.frames = keep.Count;
        p.time = new float[p.frames];
        p.cam = new Vector2[p.frames];
        p.counts = new int[p.frames];
        p.meshCounts = new int[p.frames];

        var slots = new List<Slot>();
        var spriteIndex = new Dictionary<Sprite, int>();
        var matIndex = new Dictionary<Material, int>();
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
                int s = SlotOf(e.sprite, slots, spriteIndex);
                if (s < 0) continue;
                int track;
                if (!trackIndex.TryGetValue(e.track, out track))
                {
                    if (trackIndex.Count >= ushort.MaxValue) continue;
                    track = trackIndex.Count;
                    trackIndex[e.track] = track;
                }
                p.entries.Add(new PEntry
                {
                    track = (ushort)track, sprite = (ushort)s, material = (short)MaterialOf(e.material, p, matIndex),
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

    static int SlotOf(Sprite sprite, List<Slot> slots, Dictionary<Sprite, int> index)
    {
        if (sprite == null) return -1;
        int i;
        if (index.TryGetValue(sprite, out i)) return i;
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
                    sprite = sprite, texture = tex,
                    uv = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height),
                    worldSize = r.size / ppu, srcW = r.width, srcH = r.height, srcPpu = ppu,
                    pivot = new Vector2((sprite.pivot.x - offset.x) / r.width, (sprite.pivot.y - offset.y) / r.height),
                    border = sprite.border,
                };
                i = slots.Count;
                slots.Add(slot);
            }
        }
        index[sprite] = i;
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

    // Shelf-packs the slots (shrinking the density until they fit AtlasMax) and copies them in.
    static bool PackAtlas(List<Slot> slots, Packed p)
    {
        if (slots.Count == 0) return false;
        float k = 1f;
        for (int attempt = 0; attempt < 10; attempt++, k *= .8f)
        {
            long area = 16;
            foreach (var s in slots)
            {
                float density = (Mathf.Max(s.worldSize.x, s.worldSize.y) > LargeExtent ? LargeDensity : SmallDensity) * k;
                s.w = Mathf.Clamp(Mathf.CeilToInt(s.worldSize.x * density), 1, Mathf.CeilToInt(s.srcW));
                s.h = Mathf.Clamp(Mathf.CeilToInt(s.worldSize.y * density), 1, Mathf.CeilToInt(s.srcH));
                area += (long)(s.w + 2) * (s.h + 2);
            }
            int width = area > 700000 ? 2048 : area > 180000 ? 1024 : 512;
            width = Mathf.Min(width, AtlasMax);
            int height;
            if (!Shelf(slots, width, out height)) continue;
            if (height > AtlasMax) continue;
            height = Mathf.Max(4, (height + 3) & ~3);
            p.atlasW = width;
            p.atlasH = height;
            p.atlasRgba = DrawAtlas(slots, width, height);
            if (p.atlasRgba == null) return false;
            foreach (var s in slots)
            {
                float scale = s.w / s.srcW;
                p.rects.Add(new RectInt(s.x, s.y, s.w, s.h));
                p.pivots.Add(s.pivot);
                p.ppu.Add(s.srcPpu * scale);
                p.borders.Add(s.border * scale);
            }
            return true;
        }
        return false;
    }

    static bool Shelf(List<Slot> slots, int width, out int height)
    {
        var order = new List<Slot>(slots);
        order.Sort((a, b) => b.h.CompareTo(a.h));
        int x = 4, y = 0, shelf = 4;   // the first 4x4 holds the orientation marker
        foreach (var s in order)
        {
            if (s.w + 2 > width) { height = 0; return false; }
            if (x + s.w + 1 > width) { y += shelf + 1; x = 0; shelf = 0; }
            s.x = x + 1;
            s.y = y + 1;
            x += s.w + 2;
            shelf = Mathf.Max(shelf, s.h + 2);
        }
        height = y + shelf + 1;
        return true;
    }

    static Material copyMaterial;

    // One pass of textured quads into a render texture, read back once. A white
    // marker in the bottom-left corner tells which way up the read-back came.
    static byte[] DrawAtlas(List<Slot> slots, int w, int h)
    {
        if (copyMaterial == null)
        {
            var shader = Shader.Find("Hidden/BlitCopy");
            if (shader == null) { Debug.LogWarning("[KillCam] no copy shader"); return null; }
            copyMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
        GL.PushMatrix();
        GL.LoadPixelMatrix(0f, w, 0f, h);
        Quad(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Rect(0f, 0f, 2f, 2f));
        foreach (var s in slots) Quad(s.texture, s.uv, new Rect(s.x, s.y, s.w, s.h));
        GL.PopMatrix();
        RenderTexture.active = previous;
        var bytes = ReadBack(rt, w, h, true);
        RenderTexture.ReleaseTemporary(rt);
        return bytes;
    }

    static void Quad(Texture t, Rect uv, Rect d)
    {
        copyMaterial.mainTexture = t;
        copyMaterial.SetPass(0);
        GL.Begin(GL.QUADS);
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

            byte[] png = p.atlasPng ?? Png(p.atlasRgba, p.atlasW, p.atlasH);
            w.Write(p.atlasW); w.Write(p.atlasH);
            w.Write(png.Length); w.Write(png);
            w.Write(p.rects.Count);
            for (int i = 0; i < p.rects.Count; i++)
            {
                var r = p.rects[i];
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
                    w.Write(Q(x.m00, MatQ)); w.Write(Q(x.m01, MatQ)); w.Write(Q(x.m10, MatQ)); w.Write(Q(x.m11, MatQ));
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

                p.atlasW = r.ReadInt32(); p.atlasH = r.ReadInt32();
                p.atlasPng = r.ReadBytes(r.ReadInt32());
                int sprites = r.ReadInt32();
                for (int i = 0; i < sprites; i++)
                {
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
                        x.m00 = r.ReadInt16() / MatQ; x.m01 = r.ReadInt16() / MatQ; x.m10 = r.ReadInt16() / MatQ; x.m11 = r.ReadInt16() / MatQ;
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

    // The saved clip, or null (no record, a missing / stale / corrupt / other-version file).
    public static Loaded Load()
    {
        var record = Latest;
        if (record == null) return null;
        byte[] bytes;
        try { if (!File.Exists(FilePath)) return null; bytes = File.ReadAllBytes(FilePath); }
        catch (Exception) { return null; }
        var p = Deserialize(bytes);
        if (p == null || !p.meta.SameAs(record)) return null;
        return Build(p);
    }

    public static Loaded Build(Packed p)
    {
        if (p == null || p.frames < 1) return null;
        var loaded = new Loaded { meta = p.meta };
        var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "KillCamAtlas", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        loaded.Own(atlas);
        if (p.atlasPng == null || !atlas.LoadImage(p.atlasPng, false)) { loaded.Dispose(); return null; }

        var sprites = new Sprite[p.rects.Count];
        for (int i = 0; i < sprites.Length; i++)
        {
            var r = p.rects[i];
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
