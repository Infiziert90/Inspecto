using System;
using Dalamud.Interface.Textures.TextureWraps;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace Inspecto.Data;

public record CharacterInspect : IDisposable
{
    public string Name = string.Empty;
    public uint WorldId;
    public uint Level;
    public uint JobId;
    public byte Sex;
    public uint TitleId;

    public ReadOnlySeString SearchComment;

    public uint AvgItemLevel;
    public Item?[] EquippedGear = [];

    public required IDalamudTextureWrap Image;

    public ulong ContentId;
    public uint EntityId; // This will be reset to 0 after image has been refreshed

    public DateTime Added = DateTime.Now;
    public DateTime LastUpdate = DateTime.Now;

    public ClassJob GetJob() => Sheets.ClassJobSheet.GetRow(JobId);

    public Item? GetEquippedGearByIndex(int slotIndex)
    {
        return EquippedGear.Length <= slotIndex ? null : EquippedGear[slotIndex];
    }

    public void Dispose()
    {
        Image.Dispose();
    }
}
