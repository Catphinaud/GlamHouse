using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.DalamudServices;

namespace GlamHouse;

internal sealed class GlamourTracking
{
    internal sealed record Entry(int ObjectIndex, string Name, Race Race, Gender Gender, DateTime AppliedAt, ObjectKind Kind);

    private readonly Dictionary<int, Entry> _entries = new();

    public int Count => _entries.Count;

    public void RecordApplied(int objectIndex, string name, Race race, Gender gender, ObjectKind kind)
    {
        if (objectIndex < 0) return;
        var entry = new Entry(objectIndex, name, race, gender, DateTime.UtcNow, kind);
        _entries[objectIndex] = entry;
        Svc.Log.Debug($"Tracking applied glamour for {name} ({race:G}/{gender:G}) @ {objectIndex} at {entry.AppliedAt:o} kind={kind}");
    }

    public bool IsApplied(int objectIndex) => _entries.ContainsKey(objectIndex);

    public bool TryGet(int objectIndex, out Entry entry) => _entries.TryGetValue(objectIndex, out entry!);

    public IReadOnlyCollection<Entry> All() => _entries.Values.ToList();

    public void Remove(int objectIndex)
    {
        _entries.Remove(objectIndex);
    }

    public void ClearAll()
    {
        _entries.Clear();
    }
}
