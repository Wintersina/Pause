using UnityEditor;
using UnityEngine;

// Import settings for the authored enemy death cues and scream layers
// (Audio/Resources/Audio/EnemyDeath, read by EnemyDeathAudio): short mono
// clips (~0.2-1.1 s, ~260 files as PCM), so uncompressed PCM,
// decompressed on load and preloaded -- the first kill of a run must not
// stall on a decode, and a burst of kills costs no CPU decoding.
// Sample rate preserved (authored at 44.1 kHz).
public class EnemyDeathAudioImporter : AssetPostprocessor
{
    public const string Folder = "Assets/Audio/Resources/Audio/EnemyDeath/";

    public const string ShockwaveFolder = "Assets/Audio/Resources/Audio/Shockwave/";   // the shield release whump, same rules

    public const string GateFolder = "Assets/Audio/Resources/Audio/Gate/";   // HapticGate splash cues (GateAudio), same rules

    // Bump to reimport the folder after changing a rule below.
    public override uint GetVersion() { return 3; }

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
        importer.ClearSampleSettingOverride("Android");
        importer.ClearSampleSettingOverride("iOS");
    }
}
