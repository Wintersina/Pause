using UnityEditor;
using UnityEngine;

// Import settings for the shared death clips and the rock cues
// (Audio/Resources/Audio/EnemyDeath, read by EnemyDeathAudio): short mono
// clips (~0.2-2.3 s, ~56 files as PCM), so uncompressed PCM,
// decompressed on load and preloaded -- the first kill of a run must not
// stall on a decode, and a burst of kills costs no CPU decoding.
// Sample rate preserved (the shared clips are 24 kHz, the rock cues 44.1 kHz).
public class EnemyDeathAudioImporter : AssetPostprocessor
{
    public const string Folder = "Assets/Audio/Resources/Audio/EnemyDeath/";

    public const string ShockwaveFolder = "Assets/Audio/Resources/Audio/Shockwave/";   // the shield release whump, same rules

    public const string GateFolder = "Assets/Audio/Resources/Audio/Gate/";   // HapticGate splash cues (GateAudio), same rules

    // Bump to reimport the folder after changing a rule below.
    public override uint GetVersion() { return 5; }

    public const float ShortClipSeconds = 0.5f;
    public const float AndroidVorbisQuality = 0.6f;

    // Duration from the WAV header (mono / stereo, 16 bit PCM); 1 s when unreadable.
    public static float ClipSeconds(string path)
    {
        try
        {
            using (var f = System.IO.File.OpenRead(path))
            {
                var h = new byte[44];
                if (f.Read(h, 0, 44) < 44) return 1f;
                int ch = System.BitConverter.ToInt16(h, 22), sr = System.BitConverter.ToInt32(h, 24), bits = System.BitConverter.ToInt16(h, 34);
                if (ch <= 0 || sr <= 0 || bits <= 0) return 1f;
                return (f.Length - 44f) / (sr * ch * (bits / 8f));
            }
        }
        catch { return 1f; }
    }

    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(Folder) && !assetPath.StartsWith(ShockwaveFolder) && !assetPath.StartsWith(GateFolder)) return;
        var importer = (AudioImporter)assetImporter;
        importer.forceToMono = true;
        importer.loadInBackground = false;
        importer.ambisonic = false;
        var s = importer.defaultSampleSettings;
        s.loadType = AudioClipLoadType.DecompressOnLoad;
        s.compressionFormat = AudioCompressionFormat.PCM;
        s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        s.preloadAudioData = true;
        importer.defaultSampleSettings = s;
        // Android only: the editor, desktop and iOS keep the PCM default above.
        // Clips <= ShortClipSeconds are the punchy hits (ADPCM, 4:1, stays cheap to
        // start); longer ones (screams, boss and gate tails) go Vorbis. Both stay
        // DecompressOnLoad + preloaded, so there is no first-kill decode stall.
        var a = s;
        bool shortClip = ClipSeconds(assetPath) <= ShortClipSeconds;
        a.compressionFormat = shortClip ? AudioCompressionFormat.ADPCM : AudioCompressionFormat.Vorbis;
        a.quality = AndroidVorbisQuality;
        importer.SetOverrideSampleSettings("Android", a);
        importer.ClearSampleSettingOverride("iOS");
    }
}
