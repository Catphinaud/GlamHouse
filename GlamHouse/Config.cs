using System.Collections.Generic;
using Dalamud.Configuration;
using ECommons.DalamudServices;

namespace GlamHouse;

public class Config : IPluginConfiguration
{
    public int Version { get; set; } = 4; // v4 = Presets added

    public DefaultNoArgBehavior DefaultNoArgBehavior { get; set; } = DefaultNoArgBehavior.OpenUi;

    public HashSet<RaceGenderPreset> Presets { get; set; }

    public Config()
    {
        Presets = DefaultPresets();
    }

    public void Save()
    {
        Svc.PluginInterface.SavePluginConfig(this);
    }

    private HashSet<RaceGenderPreset> DefaultPresets()
    {
        if (Version < 3) {
            var femalesOnly = new RaceGenderPreset
            {
                Name = "Females Only",
                Command = "females",
                Selections = new Dictionary<Race, GenderSelection>()
            };

            foreach (var race in new[] { Race.Hyur, Race.Elezen, Race.Lalafell, Race.Miqote, Race.Roegadyn, Race.AuRa, Race.Hrothgar, Race.Viera }) {
                femalesOnly.Selections[race] = new GenderSelection { Male = false, Female = true };
            }

            var malesOnly = new RaceGenderPreset
            {
                Name = "Males Only",
                Command = "males",
                Selections = new Dictionary<Race, GenderSelection>()
            };
            foreach (var race in new[] { Race.Hyur, Race.Elezen, Race.Lalafell, Race.Miqote, Race.Roegadyn, Race.AuRa, Race.Hrothgar, Race.Viera }) {
                malesOnly.Selections[race] = new GenderSelection { Male = true, Female = false };
            }
        }

        return new HashSet<RaceGenderPreset>();
    }
}

public enum DefaultNoArgBehavior
{
    OpenUi = 0,
    Party = 1,
    Everyone = 2,
}
