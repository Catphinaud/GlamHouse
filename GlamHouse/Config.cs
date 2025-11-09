using System.Collections.Generic;
using Dalamud.Configuration;
using ECommons.DalamudServices;

namespace GlamHouse;

public class Config : IPluginConfiguration
{
    public int Version { get; set; } = 4; // v4 = Presets added

    public DefaultNoArgBehavior DefaultNoArgBehavior { get; set; } = DefaultNoArgBehavior.OpenUi;

    public HashSet<RaceGenderPreset> Presets { get; set; } = new();

    public void Save()
    {
        Svc.PluginInterface.SavePluginConfig(this);
    }
}

public enum DefaultNoArgBehavior
{
    OpenUi = 0,
    Party = 1,
    Everyone = 2,
}
