# No ads in v1.0

Ads were removed on 2026-10-10 for v1.0. Pause ships on Google Play as a paid
($1.99) app with no ads, so the Play Console "contains ads: no" answer and the
Data-safety form (no advertising id) must stay true.

What was removed: `Scripts/UI/AdMob.cs` (a no-op stub; the Google Mobile Ads SDK
itself was already gone), every `AdMob.show()` / `hide()` / `isAdsShowwing`
call, and the AdMob component on the `UIManager` object in `startS4`. No
manifest, gradle or plugin entries for ads existed.

`Editor/Tests/NoAdsGuardTest` fails if any script, scene, prefab, manifest or
gradle file names AdMob or an ad SDK again (it is registered in `AllTests`).

For reference only, if ads ever return: the old Android banner ad unit id was
`ca-app-pub-6947554333794592/9092756042`. Bringing ads back means installing the
current Google Mobile Ads plugin, declaring ads and the AD_ID permission in the
store forms, and updating (not silencing) the guard test.
