using System;
using System.Collections.Generic;

// A store that records what it is told (the real ones need a signed-in Play Games / Game Center).
public sealed class FakeAchievementStore : IAchievementStore
{
    public bool available = true;
    public bool acceptOk = true;
    public HashSet<string> unreportable = new HashSet<string>();
    public readonly List<KeyValuePair<string, double>> reports = new List<KeyValuePair<string, double>>();

    public string Name { get { return "fake"; } }
    public bool Available { get { return available; } }
    public bool CanReport(AchievementDef def) { return !unreportable.Contains(def.id); }

    public void Report(AchievementDef def, double percent, Action<bool> done)
    {
        reports.Add(new KeyValuePair<string, double>(def.id, percent));
        if (done != null) done(acceptOk);
    }
}
