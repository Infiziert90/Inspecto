using System;
using System.Linq;
using System.Timers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Interface.Textures;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Inspecto.Data;
using Inspecto.Windows;
using Inspecto.Windows.Config;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace Inspecto;

#pragma warning disable SeStringEvaluator
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IDataManager Data { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider Hook { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static ISeStringEvaluator Evaluator { get; set; } = null!;

    private const string CommandName = "/insp";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("Inspecto");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    private bool PrintDone;
    private bool ShouldRefreshImage;
    private readonly Timer RefreshTimer = new();

    private readonly HookManager HookManager;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the main window"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

        RefreshTimer.AutoReset = false;
        RefreshTimer.Interval = Configuration.ImageRefreshTimer;
        RefreshTimer.Elapsed += (_, _) => { ShouldRefreshImage = true; };

        HookManager = new HookManager();

        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "CharacterInspect", AfterInspect);
        AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "CharacterInspect", OnInspect);
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        MainWindow.Dispose();

        HookManager.Dispose();

        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "CharacterInspect", AfterInspect);
        AddonLifecycle.UnregisterListener(AddonEvent.PostDraw, "CharacterInspect", OnInspect);

        CommandManager.RemoveHandler(CommandName);
    }

    private unsafe void OnInspect(AddonEvent type, AddonArgs args)
    {
        try
        {
            if (ShouldRefreshImage)
            {
                ShouldRefreshImage = false;
                RefreshImage();
            }

            // Data already stored, only run after Examine was closed again
            if (PrintDone)
                return;

            var agentInspect = AgentInspect.Instance();
            if (agentInspect->FetchCharacterDataStatus != 0 || agentInspect->FetchFreeCompanyStatus != 0 || agentInspect->FetchSearchCommentStatus != 0)
                return;

            // Prevent recalling
            PrintDone = true;

            var obj = ObjectTable.SearchById(agentInspect->CurrentEntityId);
            if (obj == null || !obj.IsValid() || obj is not IPlayerCharacter { ObjectKind: ObjectKind.Player } character)
            {
                Log.Warning("Unable to find EntityID for this inspect, aborting.");
                return;
            }

            var container = InventoryManager.Instance()->GetInventoryContainer(InventoryType.Examine);
            if (container is null)
                return;

            var gear = new Item?[12];
            for (var i = 0; i < 13; i++)
            {
                if (i == 5)
                    continue;

                var adjustedIndex = Utils.SlotToIndex(i);
                var slot = container->GetInventorySlot(i);

                if (slot == null || slot->ItemId == 0 || !Sheets.TryGetItem(slot->ItemId, out var itemRow))
                {
                    gear[adjustedIndex] = null;
                    continue;
                }

                gear[adjustedIndex] = itemRow;
            }

            var inspect = UIState.Instance()->Inspect;
            var characterObject = (Character*)character.Address;

            // Fill the character object with a empty texture as placeholder
            var specs = RawImageSpecification.Bgra32(720, 480);
            var textureWrap = TextureProvider.CreateEmpty(specs, false, false);

            var characterInspect = new CharacterInspect
            {
                Name = characterObject->NameString,
                WorldId = (uint) inspect.WorldId,
                Level = inspect.Level,
                JobId = inspect.ClassJobId,
                Sex = characterObject->Sex,
                TitleId = inspect.TitleId,

                SearchComment = new ReadOnlySeString(agentInspect->SearchComment),

                AvgItemLevel = inspect.AverageItemLevel,
                EquippedGear = gear.ToArray(),

                Image = textureWrap,

                ContentId = characterObject->ContentId,
                EntityId = characterObject->EntityId,
            };

            if (!MainWindow.InspectHistory.TryAdd(characterInspect.ContentId, characterInspect))
            {
                var original = MainWindow.InspectHistory[characterInspect.ContentId];

                characterInspect.Added = original.Added;
                MainWindow.InspectHistory[characterInspect.ContentId] = characterInspect;
            }

            // Start our refresh timer
            RefreshTimer.Interval = Configuration.ImageRefreshTimer;
            RefreshTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Something went wrong");
        }
    }

    private unsafe void RefreshImage()
    {
        try
        {
            var existingEntry = MainWindow.InspectHistory.Values.FirstOrDefault(h => h.EntityId == UIState.Instance()->Inspect.EntityId);
            if (existingEntry is null)
            {
                Log.Warning("Unable to find existing entry, aborting.");
                return;
            }

            // Proceed to request the correct texture from the render thread
            HookManager.RequestCharacterInpectTexture = true;

            // Delay for the texture to be fully loaded
            Framework.RunOnTick(() =>
            {
                try
                {
                    if (HookManager.RequestCharacterInpectTexture)
                    {
                        Log.Error("Still requesting image!");
                        return;
                    }

                    // This works because RenderTargetManager doesn't flush the textures
                    // 1 is the index used for Inspect
                    var image = HookManager.CharacterInspectionTexture;
                    if (image == null)
                    {
                        Log.Error("No image data was written!");
                        return;
                    }

                    if (image.Value.Data.Length > 0)
                        existingEntry.Image = TextureProvider.CreateFromRaw(RawImageSpecification.Bgra32(image.Value.Width, image.Value.Height), image.Value.Data);

                    existingEntry.EntityId = 0;

                    // Overwrite the entry with our refreshed image
                    MainWindow.InspectHistory[existingEntry.ContentId] = existingEntry;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Inner delay went wrong");
                }
            }, delayTicks: 5);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Something went wrong");
        }
    }

    private void AfterInspect(AddonEvent type, AddonArgs args)
    {
        // Reset everything for next Examine
        PrintDone = false;
        ShouldRefreshImage = false;

        RefreshTimer.Stop();
    }

    private void OnCommand(string command, string args)
    {
        ToggleMainUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();
}
