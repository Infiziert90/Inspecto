using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Colors;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Dalamud.Bindings.ImGui;
using Inspecto.Data;
using Lumina.Data.Files;
using Lumina.Excel.Sheets;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Inspecto.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin Plugin;

    public readonly Dictionary<ulong, CharacterInspect> InspectHistory = [];

    private readonly IFontHandle Miedinger;
    private readonly IDalamudTextureWrap ItemLevelTexture;
    private readonly IDalamudTextureWrap[] ItemFrameTexture = new IDalamudTextureWrap[12];
    private readonly Vector2 ItemLevelSize = new(32, 32);
    private readonly Vector2 ItemFrameSize = new(48, 48);
    private readonly Vector2 CharacterModelSize = new(300, 500);

    private const float ItemFrameSpacing = 5.0f;

    private ulong SelectedCharacter;

    public MainWindow(Plugin plugin) : base("Inspecto History")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(650, 800),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;

        Miedinger = Plugin.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Meidinger16));

        var uldWrapper = Plugin.PluginInterface.UiBuilder.LoadUld("ui/uld/Character.uld");
        ItemLevelTexture = uldWrapper.LoadTexturePart("ui/uld/Character_hr1.tex", 52)!;

        var tex = Plugin.Data.GetFile<TexFile>("ui/uld/Character_hr1.tex")!;

        var width = tex.Header.Width;
        var height = tex.Header.Height;

        var left = 0;
        var top = 144;
        var itemIconSize = 64;

        var spec = RawImageSpecification.Rgba32(itemIconSize, itemIconSize);
        using var iconTexture = Image.LoadPixelData<Rgba32>(tex.GetRgbaImageData(), width, height);
        for (var i = 0; i < 13; i++)
        {
            if (i == 5)
                continue;

            var rect = new Rectangle(left, top, itemIconSize, itemIconSize);
            using var texture = iconTexture.Clone();
            texture.Mutate(d => d
                                .Crop(rect)
                                .Invert()
                                .Resize(itemIconSize, itemIconSize));

            ItemFrameTexture[Utils.SlotToIndex(i)] = Plugin.TextureProvider.CreateFromRaw(spec, texture.ImageToRaw());

            left += itemIconSize;
            if (i == 6)
            {
                // Swap to the next line
                left = 0;
                top += itemIconSize;
            }

            if (i == 11)
            {
                // Go one back
                left -= itemIconSize;
            }
        }
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (InspectHistory.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudOrange, "You haven't inspected yet.");
            ImGui.TextColored(ImGuiColors.DalamudOrange, "History is not yet stored to disk, but planned for the future.");
            return;
        }

        var pos = ImGui.GetCursorPos();

        using (var selectionChild = ImRaii.Child("SelectionChild", new Vector2(200 * ImGuiHelpers.GlobalScale, 0), true))
        {
            if (!selectionChild.Success)
                return;

            foreach (var (key, inspectCool) in InspectHistory.OrderByDescending(i => Plugin.Configuration.SortByUpdate ? i.Value.LastUpdate : i.Value.Added))
            {
                using var id = ImRaii.PushId(key.ToString());
                if (ImGui.Selectable(inspectCool.Name, key == SelectedCharacter))
                    SelectedCharacter = key;
            }
        }

        ImGui.SetCursorPos(pos with { X = pos.X + 200 });

        using var child = ImRaii.Child("InspectedHistory", Vector2.Zero, true);
        if (!child.Success)
            return;

        if (!InspectHistory.TryGetValue(SelectedCharacter, out var inspect))
            return;

        var scaledImageSize = CharacterModelSize * ImGuiHelpers.GlobalScale;
        var scaledItemFrameSize = ItemFrameSize * ImGuiHelpers.GlobalScale;
        var scaledItemLevelSize = ItemLevelSize * ImGuiHelpers.GlobalScale;

        if (Sheets.TitleSheet.TryGetRow(inspect.TitleId, out var titleRow))
            ImGuiHelpers.SeStringWrapped(inspect.Sex == 1 ? titleRow.Feminine : titleRow.Masculine);
        ImGui.TextUnformatted($"{inspect.Name} [{inspect.GetJob().Abbreviation.ExtractText()} Level {inspect.Level}]");

        var preSearchComment = ImGui.GetCursorScreenPos();
        ImGuiHelpers.ScaledDummy(1.0f);
        ImGuiHelpers.SeStringWrapped(inspect.SearchComment);
        var postSearchComment = ImGui.GetCursorScreenPos();
        postSearchComment.X += ImGui.GetContentRegionAvail().X;
        postSearchComment.Y += ImGui.GetStyle().ItemSpacing.Y;

        var bgColor = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
        var borderColor = ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
        ImGui.GetWindowDrawList().AddRectFilled(preSearchComment - Vector2.One, postSearchComment + Vector2.One, ImGui.GetColorU32(borderColor), 5.0f);
        ImGui.GetWindowDrawList().AddRectFilled(preSearchComment, postSearchComment, ImGui.GetColorU32(bgColor), 5.0f);

        ImGui.SetCursorScreenPos(preSearchComment);
        using (ImRaii.PushIndent(5.0f))
        {
            ImGuiHelpers.ScaledDummy(1.0f);
            ImGuiHelpers.SeStringWrapped(inspect.SearchComment);
        }

        ImGuiHelpers.ScaledDummy(10.0f);
        var startPos = ImGui.GetCursorPos();

        for (var i = 0; i < 6; i++)
        {
            var currentPos = ImGui.GetCursorPos();
            ImGui.Image(ItemFrameTexture[i].Handle, scaledItemFrameSize);

            var gearInSlot = inspect.GetEquippedGearByIndex(i);
            if (gearInSlot is not null)
            {
                ImGui.SetCursorPos(currentPos);
                var iconTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(gearInSlot.Value.Icon)).GetWrapOrDefault();
                if (iconTexture is not null)
                {
                    ImGui.Image(iconTexture.Handle, scaledItemFrameSize);

                    if (ImGui.IsItemHovered())
                        GenerateItemTooltip(gearInSlot.Value);
                }

            }
        }

        var bigImageOffset = startPos.X + scaledItemFrameSize.X + ItemFrameSpacing;
        ImGui.SetCursorPos(startPos with {X = bigImageOffset});

        ImGui.Image(inspect.Image.Handle, scaledImageSize);

        var bigImageOffsetRight = bigImageOffset + scaledImageSize.X + ItemFrameSpacing;
        ImGui.SetCursorPos(startPos with {X = bigImageOffsetRight});
        for (var i = 6; i < 12; i++)
        {
            var currentPos = ImGui.GetCursorPos();
            ImGui.Image(ItemFrameTexture[i].Handle, scaledItemFrameSize);

            var gearInSlot = inspect.GetEquippedGearByIndex(i);
            if (gearInSlot is not null)
            {
                ImGui.SetCursorPos(currentPos);
                var iconTexture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(gearInSlot.Value.Icon)).GetWrapOrDefault();
                if (iconTexture is not null)
                {
                    ImGui.Image(iconTexture.Handle, scaledItemFrameSize);

                    if (ImGui.IsItemHovered())
                        GenerateItemTooltip(gearInSlot.Value);
                }
            }

            ImGui.SetCursorPos(ImGui.GetCursorPos() with {X = bigImageOffsetRight});
        }

        using var fontPushed = Miedinger.Push();
        var textSize = ImGui.CalcTextSize("0000");
        ImGui.SetCursorPos(new Vector2(bigImageOffset + scaledImageSize.X - textSize.X, startPos.Y + (scaledItemLevelSize.Y / 2 - textSize.Y / 2)));
        ImGui.TextUnformatted($"{inspect.AvgItemLevel:0000}");

        ImGui.SetCursorPos(startPos with { X = bigImageOffset + scaledImageSize.X - textSize.X - scaledItemLevelSize.X });
        ImGui.Image(ItemLevelTexture.Handle, scaledItemLevelSize);
    }

    private void GenerateItemTooltip(Item item)
    {
        using (ImRaii.Tooltip())
        using (ImRaii.TextWrapPos(ImGui.GetFontSize() * 200.0f))
        {
            Helper.WrappedTextWithColor(ImGuiColors.ParsedGreen, Plugin.Evaluator.Evaluate(item.Name).ExtractText());
            ImGuiHelpers.ScaledDummy(1.0f);
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(1.0f);
            ImGui.TextUnformatted($"Item Level: {item.LevelItem.RowId}");
            ImGui.TextUnformatted($"Lv: {item.LevelEquip}");
        }
    }
}
