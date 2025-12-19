using System;
using System.Runtime.CompilerServices;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TerraFX.Interop.DirectX;

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

        var device = (ID3D11Device*)Plugin.PluginInterface.UiBuilder.DeviceHandle;
        var texture = (ID3D11Texture2D*)tex->D3D11Texture2D;

        D3D11_TEXTURE2D_DESC desc;
        texture->GetDesc(&desc);

        desc.BindFlags = 0;
        desc.CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ;
        desc.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
        desc.MiscFlags = 0;
        desc.MipLevels = 1;

        ID3D11Texture2D* stagingTexture;
        if (device->CreateTexture2D(&desc, null, &stagingTexture) < 0)
            return ([], 0, 0);

        ID3D11DeviceContext* context;
        device->GetImmediateContext(&context);

        context->CopyResource((ID3D11Resource*)stagingTexture, (ID3D11Resource*)texture);

        D3D11_MAPPED_SUBRESOURCE mapped;
        if (context->Map((ID3D11Resource*)stagingTexture, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped) < 0)
        {
            stagingTexture->Release();
            return ([], 0, 0);
        }

        var sourcePtr = (nint)mapped.pData;
        var rowPitch = mapped.RowPitch;
        var image = new Image<Bgra32>((int)desc.Width, (int)desc.Height);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var destSpan = accessor.GetRowSpan(y);
                var src = (byte*)sourcePtr + y * rowPitch;
                Buffer.MemoryCopy(src, Unsafe.AsPointer(ref destSpan[0]), destSpan.Length * 4, destSpan.Length * 4);
            }
        });

        context->Unmap((ID3D11Resource*)stagingTexture, 0);
        stagingTexture->Release();

        var data = image.ImageToRaw();
        return (data, image.Width, image.Height);
    }
}