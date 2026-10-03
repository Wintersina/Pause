// Game Center saved games for GameCenterAccount.cs.
//
// Unity's Social API covers sign-in, achievements and leaderboards but not
// GKSavedGame, so this exposes the three calls the game needs. Every callback
// is delivered on the main queue, which is Unity's player-loop thread on iOS.
//
// Saved games live in the player's iCloud: the app needs the Game Center and
// iCloud (iCloud Documents) capabilities, which the iOS post-process build
// step adds to the Xcode project.

#import <Foundation/Foundation.h>
#import <GameKit/GameKit.h>
#include <stdlib.h>
#include <string.h>

typedef void (*PauseGCLoadCallback)(int ok, const char* json);
typedef void (*PauseGCWriteCallback)(int ok);

static char* PauseGCCopyString(NSString* s)
{
    if (s == nil) return NULL;
    const char* utf8 = [s UTF8String];
    if (utf8 == NULL) return NULL;
    char* copy = (char*)malloc(strlen(utf8) + 1);
    strcpy(copy, utf8);
    return copy;   // IL2CPP frees returned strings with free()
}

static void PauseGCDeliverLoad(PauseGCLoadCallback callback, BOOL ok, NSData* data)
{
    NSString* json = nil;
    if (data != nil && data.length > 0)
        json = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    dispatch_async(dispatch_get_main_queue(), ^{
        if (callback != NULL) callback(ok ? 1 : 0, json != nil ? [json UTF8String] : NULL);
    });
}

extern "C" {

const char* PauseGC_PlayerId()
{
    GKLocalPlayer* player = [GKLocalPlayer localPlayer];
    if (!player.isAuthenticated) return NULL;
    return PauseGCCopyString(player.gamePlayerID);
}

void PauseGC_LoadSave(const char* name, PauseGCLoadCallback callback)
{
    NSString* saveName = [NSString stringWithUTF8String:(name != NULL ? name : "")];
    GKLocalPlayer* player = [GKLocalPlayer localPlayer];
    if (!player.isAuthenticated) { PauseGCDeliverLoad(callback, NO, nil); return; }

    [player fetchSavedGamesWithCompletionHandler:^(NSArray<GKSavedGame*>* games, NSError* error) {
        if (error != nil)
        {
            NSLog(@"[CloudSave] Game Center fetch failed: %@", error.localizedDescription);
            PauseGCDeliverLoad(callback, NO, nil);
            return;
        }
        NSMutableArray<GKSavedGame*>* matching = [NSMutableArray array];
        for (GKSavedGame* game in games)
            if ([game.name isEqualToString:saveName]) [matching addObject:game];
        if (matching.count == 0) { PauseGCDeliverLoad(callback, YES, nil); return; }

        // More than one copy means two devices saved while apart: keep the
        // most recent one and fold the others into it.
        GKSavedGame* newest = matching[0];
        for (GKSavedGame* game in matching)
            if ([game.modificationDate compare:newest.modificationDate] == NSOrderedDescending) newest = game;

        [newest loadDataWithCompletionHandler:^(NSData* data, NSError* loadError) {
            if (loadError != nil)
            {
                NSLog(@"[CloudSave] Game Center load failed: %@", loadError.localizedDescription);
                PauseGCDeliverLoad(callback, NO, nil);
                return;
            }
            if (matching.count > 1 && data != nil)
            {
                [player resolveConflictingSavedGames:matching withData:data
                                   completionHandler:^(NSArray<GKSavedGame*>* resolved, NSError* resolveError) {
                    if (resolveError != nil)
                        NSLog(@"[CloudSave] Game Center conflict resolution failed: %@", resolveError.localizedDescription);
                }];
            }
            PauseGCDeliverLoad(callback, YES, data);
        }];
    }];
}

void PauseGC_WriteSave(const char* name, const char* json, PauseGCWriteCallback callback)
{
    NSString* saveName = [NSString stringWithUTF8String:(name != NULL ? name : "")];
    NSData* data = [NSData dataWithBytes:(json != NULL ? json : "") length:(json != NULL ? strlen(json) : 0)];
    GKLocalPlayer* player = [GKLocalPlayer localPlayer];
    if (!player.isAuthenticated)
    {
        dispatch_async(dispatch_get_main_queue(), ^{ if (callback != NULL) callback(0); });
        return;
    }
    [player saveGameData:data withName:saveName completionHandler:^(GKSavedGame* saved, NSError* error) {
        if (error != nil) NSLog(@"[CloudSave] Game Center save failed: %@", error.localizedDescription);
        dispatch_async(dispatch_get_main_queue(), ^{ if (callback != NULL) callback(error == nil ? 1 : 0); });
    }];
}

}
