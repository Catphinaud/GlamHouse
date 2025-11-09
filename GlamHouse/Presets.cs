using System;
using System.Collections.Generic;

namespace GlamHouse;

public sealed class GenderSelection
{
    public bool Male { get; set; } = true;
    public bool Female { get; set; } = true;
}

public sealed class RaceGenderPreset : IEquatable<RaceGenderPreset>
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = "New Preset";
    public string Command { get; set; } = "newpreset";

    // If a race is missing, it is considered disabled.
    public Dictionary<Race, GenderSelection> Selections { get; set; } = new();

    public TargetScope Scope { get; set; } = TargetScope.All;

    public bool IsAllowed(Race race, Gender gender)
    {
        if (race == Race.Unknown || gender == Gender.Unknown) return false;
        if (!Selections.TryGetValue(race, out var sel)) return false;
        return gender switch
        {
            Gender.Male => sel.Male,
            Gender.Female => sel.Female,
            _ => false
        };
    }

    public bool Equals(RaceGenderPreset? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id.Equals(other.Id);
    }

    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj) || obj is RaceGenderPreset other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
