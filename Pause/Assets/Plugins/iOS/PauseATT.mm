// App Tracking Transparency for the ads flow (AdMobProvider.RequestTracking), asked right after
// Google's UMP consent form. Fire-and-poll: PauseATTRequest() starts the system prompt, C# polls
// PauseATTStatus() (0 not decided yet, 1 restricted, 2 denied, 3 authorized, 99 not available).
// The prompt only appears while the app is active, so a request made earlier waits for that.
// NSUserTrackingUsageDescription (the prompt's text) comes from AdsConfig.TrackingUsageDescription
// via the Google Mobile Ads settings (AdsBuildSync) and the plugin's plist step.

#import <UIKit/UIKit.h>
#import <AppTrackingTransparency/AppTrackingTransparency.h>

static int gPauseATTStatus = 0;
static BOOL gPauseATTRequested = NO;

static void PauseATTAsk(void)
{
    if (@available(iOS 14, *))
    {
        [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
            gPauseATTStatus = (int)status;
        }];
    }
    else gPauseATTStatus = 99;
}

extern "C" void PauseATTRequest(void)
{
    if (gPauseATTRequested) return;
    gPauseATTRequested = YES;
    dispatch_async(dispatch_get_main_queue(), ^{
        if ([UIApplication sharedApplication].applicationState == UIApplicationStateActive) { PauseATTAsk(); return; }
        __block id token = [[NSNotificationCenter defaultCenter] addObserverForName:UIApplicationDidBecomeActiveNotification
                                                                              object:nil
                                                                               queue:[NSOperationQueue mainQueue]
                                                                          usingBlock:^(NSNotification* note) {
            [[NSNotificationCenter defaultCenter] removeObserver:token];
            PauseATTAsk();
        }];
    });
}

extern "C" int PauseATTStatus(void)
{
    if (@available(iOS 14, *))
    {
        ATTrackingManagerAuthorizationStatus s = [ATTrackingManager trackingAuthorizationStatus];
        if (s != ATTrackingManagerAuthorizationStatusNotDetermined) return (int)s;   // already answered (now or in an earlier launch)
        return gPauseATTStatus;
    }
    return 99;
}
