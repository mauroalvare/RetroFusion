namespace Retro.Systems.Pc;

public static class PcFirmware
{
    public static string FindPath(string folder)
    {
        string nested = Path.Combine(folder, "pcxt", "pcxt-bios.rom");
        string flat = Path.Combine(folder, "pcxt-bios.rom");
        if (File.Exists(nested)) return nested;
        if (File.Exists(flat)) return flat;
        throw new FileNotFoundException("Falta el BIOS de PC. Guardá un BIOS compatible como firmware/pcxt/pcxt-bios.rom.");
    }
    public static byte[] Read(string folder) => File.ReadAllBytes(FindPath(folder));
    public static bool IsSupportedDiskSize(int size) => size is 163840 or 184320 or 327680 or 368640 or 737280;
}