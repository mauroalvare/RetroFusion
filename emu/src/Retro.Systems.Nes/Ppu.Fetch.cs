namespace Retro.Systems.Nes;

public sealed partial class Ppu
{
    // The scanline renderer reads tiles in a batch. Only this dot schedule and CPU
    // register accesses drive the cartridge's physical A12 input.
    private void ClockFetchAddress()
    {
        if ((Dot & 1) == 0 || Dot == 0) return;
        int phase = (Dot - 1) & 7;
        if (Dot is <= 256 or >= 321 and <= 336)
        {
            int v = _v, tiles = Dot <= 256 ? (Dot - 1) / 8 : (Dot - 321) / 8;
            int coarse = (v & 31) + tiles;
            v = (v & ~31) | (coarse & 31);
            if (coarse >= 32) v ^= 0x400;
            int nt = 0x2000 | (v & 0xFFF);
            int address = phase switch
            {
                0 => nt,
                2 => 0x23C0 | (v & 0xC00) | ((v >> 4) & 0x38) | ((v >> 2) & 7),
                _ => ((_ctrl & 0x10) != 0 ? 0x1000 : 0) + Read((ushort)nt) * 16 + ((v >> 12) & 7) + (phase == 6 ? 8 : 0)
            };
            _cart.ObservePpuAddress((ushort)address);
        }
        else if (Dot <= 320)
        {
            if (phase < 4) { _cart.ObservePpuAddress((ushort)(0x2000 | (_v & 0xFFF))); return; }
            int slot = (Dot - 257) / 8, nextLine = Line == PreRenderLine ? 0 : Line + 1;
            int height = (_ctrl & 0x20) != 0 ? 16 : 8, selected = -1, count = 0;
            for (int i = 0; i < 64; i++)
            {
                int row = nextLine - (Oam[i * 4] + 1);
                if (row < 0 || row >= height) continue;
                if (count++ == slot) { selected = i; break; }
            }
            int tile = selected < 0 ? 255 : Oam[selected * 4 + 1];
            int y = selected < 0 ? 0 : nextLine - (Oam[selected * 4] + 1);
            if (selected >= 0 && (Oam[selected * 4 + 2] & 0x80) != 0) y = height - 1 - y;
            int address = height == 16 ? (tile & 1) * 0x1000 + ((tile & 0xFE) + (y >> 3)) * 16 :
                ((_ctrl & 8) != 0 ? 0x1000 : 0) + tile * 16;
            _cart.ObservePpuAddress((ushort)(address + (y & 7) + (phase == 6 ? 8 : 0)));
        }
        else _cart.ObservePpuAddress((ushort)(0x2000 | (_v & 0xFFF)));
    }
}