using Dalamud.Configuration;
using System;

namespace Inspecto;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public int ImageRefreshTimer = 500; // in ms
    public bool SortByUpdate = false;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
