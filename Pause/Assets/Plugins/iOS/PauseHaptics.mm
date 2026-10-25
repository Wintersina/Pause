// The death haptic for DeathHaptics.cs: a heavy thump at the fatal hit, then a
// short fading rumble (two softer taps), about 0.2 s in all. UIKit feedback
// generators only; no entitlement or capability needed. Silently does nothing
// on devices without a Taptic Engine (iPads, the simulator).

#import <UIKit/UIKit.h>

extern "C" void PauseHapticDeath(void)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        UIImpactFeedbackGenerator* heavy = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
        UIImpactFeedbackGenerator* soft = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
        [heavy prepare];
        [soft prepare];
        [heavy impactOccurred];
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.09 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
            if (@available(iOS 13.0, *)) [soft impactOccurredWithIntensity:0.6]; else [soft impactOccurred];
        });
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.17 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
            if (@available(iOS 13.0, *)) [soft impactOccurredWithIntensity:0.3]; else [soft impactOccurred];
        });
    });
}
