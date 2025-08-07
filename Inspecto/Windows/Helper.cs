using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;

namespace Inspecto.Windows;

public static class Helper
{
    /// <summary>
    /// An unformatted version for Helper.TextColored
    /// </summary>
    /// <param name="color">color to be used</param>
    /// <param name="text">text to display</param>
    public static void TextColored(Vector4 color, string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(text);
    }

    /// <summary>
    /// An unformatted version for Helper.Tooltip
    /// </summary>
    /// <param name="tooltip">tooltip to display</param>
    public static void Tooltip(string tooltip)
    {
        using (ImRaii.Tooltip())
        using (ImRaii.TextWrapPos(ImGui.GetFontSize() * 35.0f))
            ImGui.TextUnformatted(tooltip);
    }

    /// <summary>
    /// An unformatted version for ImGui.TextWrapped
    /// </summary>
    /// <param name="text">text to display</param>
    public static void TextWrapped(string text)
    {
        using (ImRaii.TextWrapPos(0.0f))
            ImGui.TextUnformatted(text);
    }

    /// <summary>
    /// An unformatted version for ImGui.TextWrapped with color
    /// </summary>
    /// <param name="color">color to be used</param>
    /// <param name="text">text to display</param>
    public static void WrappedTextWithColor(Vector4 color, string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            TextWrapped(text);
    }

    /// <summary>
    /// An unformatted version for ImGui.BulletText
    /// </summary>
    /// <param name="text">text to display</param>
    public static void BulletText(string text)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextUnformatted(text);
    }
}
