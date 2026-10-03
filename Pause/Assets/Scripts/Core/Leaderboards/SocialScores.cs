using System;
using System.Collections.Generic;
using UnityEngine.SocialPlatforms;

// Turns Unity Social API scores (both Play Games and Game Center hand out
// IScore) into a LeaderboardPage, resolving display names in one LoadUsers
// round trip. A failed name lookup still shows the scores, just unnamed.
public static class SocialScores
{
    public static void ToPage(IScore[] scores, IScore playerScore, string localUserId, string localUserName,
                              Action<string[], Action<IUserProfile[]>> loadUsers, Action<LeaderboardPage> done)
    {
        var page = new LeaderboardPage { status = LeaderboardStatus.Ok };
        if (scores != null)
            foreach (var s in scores)
                if (s != null) page.entries.Add(Entry(s, localUserId));
        if (playerScore != null && playerScore.rank > 0)
        {
            page.hasPlayer = true;
            page.player = Entry(playerScore, localUserId);
            page.player.isLocalPlayer = true;
        }

        var ids = new List<string>();
        foreach (var e in page.entries)
            if (!string.IsNullOrEmpty(e.playerId) && !e.isLocalPlayer && !ids.Contains(e.playerId)) ids.Add(e.playerId);

        Action finish = () =>
        {
            for (int i = 0; i < page.entries.Count; i++)
            {
                var e = page.entries[i];
                if (e.isLocalPlayer && !string.IsNullOrEmpty(localUserName)) e.playerName = localUserName;
                if (string.IsNullOrEmpty(e.playerName)) e.playerName = "Pilot " + e.rank;
                page.entries[i] = e;
            }
            if (page.hasPlayer && !string.IsNullOrEmpty(localUserName)) page.player.playerName = localUserName;
            done(page);
        };

        if (ids.Count == 0 || loadUsers == null) { finish(); return; }
        try
        {
            loadUsers(ids.ToArray(), profiles =>
            {
                var names = new Dictionary<string, string>();
                if (profiles != null)
                    foreach (var p in profiles)
                        if (p != null && !string.IsNullOrEmpty(p.id)) names[p.id] = p.userName;
                for (int i = 0; i < page.entries.Count; i++)
                {
                    var e = page.entries[i];
                    string name;
                    if (e.playerId != null && names.TryGetValue(e.playerId, out name)) e.playerName = name;
                    page.entries[i] = e;
                }
                finish();
            });
        }
        catch (Exception) { finish(); }
    }

    static LeaderboardEntry Entry(IScore s, string localUserId)
    {
        return new LeaderboardEntry
        {
            rank = s.rank,
            playerId = s.userID,
            value = s.value,
            isLocalPlayer = !string.IsNullOrEmpty(localUserId) && s.userID == localUserId,
        };
    }
}
