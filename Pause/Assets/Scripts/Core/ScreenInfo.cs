using System;
using UnityEngine;

// The one place layout code reads the screen from: its pixel size, the safe
// area (the part clear of the status bar, notch / Dynamic Island / punch-hole,
// rounded corners' insets, and the home indicator or gesture bar) and the
// display cutouts themselves, plus its reported density (dpi) and whether it
// is an iOS device (points vs Android dp: UiScale turns these into physical
// sizes).
//
// On a device these are simply Screen.width / height / safeArea / cutouts.
// The editor cannot set those, so anything that lays a screen out goes
// through here and the screen-fit suite (ScreenFitTest, ScreenFitSheets)
// swaps in a synthetic phone with ScreenInfo.Override(...): every screen can
// then be laid out and checked against any device shape without a build.
//
// All rects are Unity screen pixels, origin bottom-left, like Screen.safeArea.
public static class ScreenInfo
{
    static bool overridden;
    static int width, height;
    static Rect safeArea;
    static Rect[] cutouts = new Rect[0];
    static float dpi;
    static bool ios;

    public static bool Overridden { get { return overridden; } }
    public static int Width { get { return overridden ? width : Screen.width; } }
    public static int Height { get { return overridden ? height : Screen.height; } }
    public static Rect SafeArea { get { return overridden ? safeArea : Screen.safeArea; } }
    public static Rect[] Cutouts { get { return overridden ? cutouts : Screen.cutouts; } }
    public static Vector2 Size { get { return new Vector2(Width, Height); } }
    // As the OS reports it: 0 (or nonsense) when it does not. UiScale.PxPerPoint
    // decides what to believe.
    public static float Dpi { get { return overridden ? dpi : Screen.dpi; } }
    public static bool IsIos { get { return overridden ? ios : Application.platform == RuntimePlatform.IPhonePlayer; } }

    // The insets of the safe area from each screen edge, in pixels (never negative).
    public static float SafeTopInset { get { return Mathf.Max(0f, Height - SafeArea.yMax); } }
    public static float SafeBottomInset { get { return Mathf.Max(0f, SafeArea.yMin); } }
    public static float SafeLeftInset { get { return Mathf.Max(0f, SafeArea.xMin); } }
    public static float SafeRightInset { get { return Mathf.Max(0f, Width - SafeArea.xMax); } }

    // Editor / tests only: pretend to be a `w` x `h` screen with this safe
    // area and these cutouts until the returned scope is disposed.
    // `reportedDpi` 0 = the OS reports no density (UiScale's fallback).
    public static IDisposable Override(int w, int h, Rect safe, Rect[] cutoutRects = null, float reportedDpi = 0f, bool iosDevice = false)
    {
        var scope = new Scope(overridden, width, height, safeArea, cutouts, dpi, ios);
        overridden = true;
        width = w;
        height = h;
        safeArea = safe;
        cutouts = cutoutRects ?? new Rect[0];
        dpi = reportedDpi;
        ios = iosDevice;
        return scope;
    }

    public static void ClearOverride()
    {
        overridden = false;
        cutouts = new Rect[0];
    }

    sealed class Scope : IDisposable
    {
        readonly bool was;
        readonly int w, h;
        readonly Rect safe;
        readonly Rect[] cuts;
        readonly float d;
        readonly bool i;

        public Scope(bool was, int w, int h, Rect safe, Rect[] cuts, float d, bool i)
        {
            this.was = was; this.w = w; this.h = h; this.safe = safe; this.cuts = cuts; this.d = d; this.i = i;
        }

        public void Dispose()
        {
            overridden = was; width = w; height = h; safeArea = safe; cutouts = cuts; dpi = d; ios = i;
        }
    }
}
