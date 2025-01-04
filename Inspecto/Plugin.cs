using System;
using System.IO;
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
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Inspecto.Data;
using Inspecto.Windows;
using Inspecto.Windows.Config;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace Inspecto;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/insp";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("Inspecto");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    private bool PrintDone;
    private bool ShouldRefreshImage;
    private readonly Timer RefreshTimer = new();

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

        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "CharacterInspect", AfterInspect);
        AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "CharacterInspect", OnInspect);
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        MainWindow.Dispose();

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

            var texture = RenderTargetManager.Instance()->GetCharaViewTexture(1);
            var image = GetKernelTextureAsImage(texture);
            var specs = RawImageSpecification.Bgra32(image.Width, image.Height);

            var textureWrap = image.Data.Length > 0 ? TextureProvider.CreateFromRaw(specs, image.Data) : TextureProvider.CreateEmpty(specs, false, false);

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

            // This works because RenderTargetManager doesn't flush the textures
            // 1 is the index used for Inspect
            var texture = RenderTargetManager.Instance()->GetCharaViewTexture(1);
            var image = GetKernelTextureAsImage(texture);
            if (image.Data.Length > 0)
                existingEntry.Image = TextureProvider.CreateFromRaw(RawImageSpecification.Bgra32(image.Width, image.Height), image.Data);

            existingEntry.EntityId = 0;

            // Overwrite the entry with our refreshed image
            MainWindow.InspectHistory[existingEntry.ContentId] = existingEntry;
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

    private unsafe (byte[] Data, int Width, int Height) GetKernelTextureAsImage(Texture* tex)
    {
        if (tex == null || tex->D3D11Texture2D == null)
            return ([], 0, 0);

        var device = PluginInterface.UiBuilder.Device;
        var texture = CppObject.FromPointer<Texture2D>((nint)tex->D3D11Texture2D);

        // thanks to ChatGPT
        // Get the texture description
        var desc = texture.Description;

        // Create a staging texture with the same description
        using var stagingTexture = new Texture2D(device, new Texture2DDescription()
        {
            ArraySize = 1,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            Format = desc.Format,
            Height = desc.Height,
            Width = desc.Width,
            MipLevels = 1,
            OptionFlags = desc.OptionFlags,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging
        });

        // Copy the texture data to the staging texture
        device.ImmediateContext.CopyResource(texture, stagingTexture);

        // Map the staging texture
        device.ImmediateContext.MapSubresource(stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out var dataStream);

        using var pixelDataStream = new MemoryStream();
        dataStream.CopyTo(pixelDataStream);

        // Unmap the staging texture
        device.ImmediateContext.UnmapSubresource(stagingTexture, 0);

        var data = Image.LoadPixelData<Bgra32>(pixelDataStream.ToArray(), desc.Width, desc.Height).ImageToRaw();
        return (data, desc.Width, desc.Height);
    }

    private void OnCommand(string command, string args)
    {
        ToggleMainUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();
}
