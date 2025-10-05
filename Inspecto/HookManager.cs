using System;
using System.IO;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Inspecto;

public unsafe class HookManager : IDisposable
{
    // From: https://github.com/MidoriKami/Mappy/blob/master/Mappy/MapRenderer/MapRenderer.Fog.cs#L28
    [Signature("E8 ?? ?? ?? ?? 48 8B 4B 30 FF 15 ?? ?? ?? ??", DetourName = nameof(OnImmediateContextProcessCommands))]
    private readonly Hook<ImmediateContextProcessCommands>? ImmediateContextProcessCommandsHook = null;
    private delegate void ImmediateContextProcessCommands(ImmediateContext* commands, RenderCommandBufferGroup* bufferGroup, uint a3);

    private int FrameCounter;

    public bool RequestCharacterInpectTexture;
    public (byte[] Data, int Width, int Height)? CharacterInspectionTexture;

    public HookManager()
    {
        Plugin.Hook.InitializeFromAttributes(this);
        ImmediateContextProcessCommandsHook?.Enable();
    }

    public void Dispose()
    {
        ImmediateContextProcessCommandsHook?.Dispose();
    }

    private void OnImmediateContextProcessCommands(ImmediateContext* commands, RenderCommandBufferGroup* bufferGroup, uint a3)
    {
        try
        {
            // Delay by a certain number of frames because the game hasn't loaded the new texture yet.
            if (RequestCharacterInpectTexture && FrameCounter++ == 2)
            {
                CharacterInspectionTexture = null;
                CharacterInspectionTexture = GetKernelTextureAsImage();
                RequestCharacterInpectTexture = false;
                FrameCounter = 0;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Exception during OnImmediateContextProcessCommands");
        }

        ImmediateContextProcessCommandsHook!.Original(commands, bufferGroup, a3);
    }

    private (byte[] Data, int Width, int Height) GetKernelTextureAsImage()
    {
        // This works because RenderTargetManager doesn't flush the textures
        // 1 is the index used for Inspect
        var tex = RenderTargetManager.Instance()->GetCharaViewTexture(1);
        if (tex == null || tex->D3D11Texture2D == null)
            return ([], 0, 0);

        var device = Plugin.PluginInterface.UiBuilder.Device;
        var texture = CppObject.FromPointer<Texture2D>((nint)tex->D3D11Texture2D);

        var desc = new Texture2DDescription {
            ArraySize = 1,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            Format = texture.Description.Format,
            Height = texture.Description.Height,
            Width = texture.Description.Width,
            MipLevels = 1,
            OptionFlags = texture.Description.OptionFlags,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging
        };

        using var stagingTexture = new Texture2D(device, desc);
        var context = device.ImmediateContext;

        context.CopyResource(texture, stagingTexture);
        device.ImmediateContext.MapSubresource(stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out var dataStream);

        using var pixelDataStream = new MemoryStream();
        dataStream.CopyTo(pixelDataStream);
        dataStream.Dispose();

        var data = Image.LoadPixelData<Bgra32>(pixelDataStream.ToArray(), desc.Width, desc.Height).ImageToRaw();
        return (data, desc.Width, desc.Height);
    }
}