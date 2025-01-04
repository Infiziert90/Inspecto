using System;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;

namespace Inspecto.Windows.Config;

public partial class ConfigWindow
{
    private void Settings()
    {
        using var tabItem = ImRaii.TabItem("Settings");
        if (!tabItem.Success)
            return;

        var changed = false;


        var timer = Plugin.Configuration.ImageRefreshTimer;
        ImGui.TextUnformatted("Try Refresh Image After X ms");
        ImGui.SetNextItemWidth(ImGui.GetWindowWidth() / 3.0f);
        if (ImGui.InputInt("##RefreshInput", ref timer, 50, 500))
        {
            if (timer != Plugin.Configuration.ImageRefreshTimer)
            {
                Plugin.Configuration.ImageRefreshTimer = Math.Clamp(timer, 100, 5000); // ms
                changed = true;
            }
        }

        changed |= ImGui.Checkbox("Sort By Last Updated", ref Plugin.Configuration.SortByUpdate);

        if (changed)
            Plugin.Configuration.Save();
    }
}
