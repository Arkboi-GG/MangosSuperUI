using System.Buffers.Binary;
using System.Text;
using MangosSuperUI.Services;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class M2ParticleHueShiftTests
{
    [Fact]
    public void HueShift_ChangesOnlyInlineRampRgb_AndPreservesAlphaMotionAndFileLayout()
    {
        const int emitterOffset = 0x200;
        const int colorOffset = emitterOffset + 0x150;
        var source = new byte[0x400];
        Encoding.ASCII.GetBytes("MD20").CopyTo(source, 0);
        WriteU32(source, 0x04, 256);
        WriteU32(source, 0x13C, 1);
        WriteU32(source, 0x140, emitterOffset);
        WriteU32(source, emitterOffset, uint.MaxValue); // stock -1 particle id

        uint[] originalRamp =
        [
            0xFF00CC00, // bright green
            0x80006600, // darker green, authored alpha 0x80
            0x20002200, // dark tail, authored alpha 0x20
        ];
        for (int i = 0; i < originalRamp.Length; i++)
            WriteU32(source, colorOffset + i * 4, originalRamp[i]);

        // Patterned bytes stand in for every timing/motion/global-sequence value that must survive.
        for (int i = emitterOffset + 0x2C; i < emitterOffset + 0x150; i++)
            source[i] = unchecked((byte)(i * 29 + 7));
        byte[] original = (byte[])source.Clone();

        byte[]? shifted = M2ParticlePatcher.PatchParticles(source,
            new M2ParticlePatcher.ParticlePatchParams
            {
                UseHueShift = true,
                HueShiftColor = 0x00FF00FF, // magenta target hue
            });

        Assert.NotNull(shifted);
        Assert.Equal(original, source);
        Assert.Equal(original.Length, shifted!.Length);

        var allowedChanges = Enumerable.Range(colorOffset, 12).ToHashSet();
        for (int i = 0; i < original.Length; i++)
        {
            if (!allowedChanges.Contains(i))
                Assert.Equal(original[i], shifted[i]);
        }

        for (int i = 0; i < originalRamp.Length; i++)
        {
            uint recolored = ReadU32(shifted, colorOffset + i * 4);
            Assert.Equal(originalRamp[i] >> 24, recolored >> 24); // alpha unchanged
            Assert.NotEqual(originalRamp[i] & 0x00FFFFFF, recolored & 0x00FFFFFF);
            Assert.True(((recolored >> 16) & 0xFF) > 0);
            Assert.True((recolored & 0xFF) > 0);
        }
    }

    private static uint ReadU32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static void WriteU32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
}
