using UnityEngine;

// Art the codex needs that lives outside any Resources folder. The Space
// world draws the scene's own authored backdrop (gameS1's starsBackground0
// material) rather than a Resources/Worlds texture, so this asset, loaded
// from Resources/Codex/CodexArtRefs, points at that same texture.
public class CodexArtRefs : ScriptableObject
{
    public Texture2D spaceBackdrop;
}
