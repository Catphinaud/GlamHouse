using System.Collections.Generic;
using Dalamud.Configuration;
using ECommons.DalamudServices;

namespace GlamHouse;

public class Config : IPluginConfiguration
{
    public int Version { get; set; } = 2; // bumped for new default no-arg command behavior option

    public DefaultNoArgBehavior DefaultNoArgBehavior { get; set; } = DefaultNoArgBehavior.OpenUi;

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
