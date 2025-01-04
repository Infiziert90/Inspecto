using System;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Inspecto;

public static class Utils
{
    public static uint NormalizeItemId(uint itemId)
    {
        return itemId > 1_000_000 ? itemId - 1_000_000 : itemId > 500_000 ? itemId - 500_000 : itemId;
    }

    public static byte[] ImageToRaw(this Image<Bgra32> image)
    {
        var data = new byte[4 * image.Width * image.Height];
        image.CopyPixelDataTo(data);
        return data;
    }

    public static byte[] ImageToRaw(this Image<Rgba32> image)
    {
        var data = new byte[4 * image.Width * image.Height];
        image.CopyPixelDataTo(data);
        return data;
    }

    public static int SlotToIndex(int id)
    {
        if (id == 5)
            throw new Exception("5 is not a valid slot id, it was used by belts!!!");

        return id switch
        {
            0 => 0,   // Main Weapon
            1 => 6,   // Shield
            2 => 1,   // Head
            3 => 2,   // Chest
            4 => 3,   // Hands
            6 => 4,   // Pants
            7 => 5,   // Shoes
            8 => 7,   // Earrings
            9 => 8,   // Necklace
            10 => 9,  // Bracelet
            11 => 10, // Right Ring
            12 => 11, // Left Ring
            _ => throw new UnreachableException()
        };
    }
}
