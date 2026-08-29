using System.Buffers.Binary;
using System.Text;
using MangosSuperUI.Services.WeaponForge;
using MangosSuperUI.Services.WeaponForge.RawM2;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class M2Type0TextureRepointTests
{
    private const int TextureTableOffset = 0x160;

    [Fact]
    public void LongerPath_AppendsAndChangesOnlySelectedFilenameFieldsInOriginalPrefix()
    {
        byte[] source = VanillaTextureFixture();
        byte[] original = (byte[])source.Clone();
        string replacement = WeaponNaming.EffectTextureMpqPath(4321, 1);
        int selectedRecord = TextureTableOffset + 16;

        byte[] rewritten = M2GeometryPatcher.RewriteHardcodedTexturePaths(source,
            new Dictionary<int, string> { [1] = replacement });

        Assert.Equal(original, source); // caller's stock bytes were not mutated
        Assert.True(rewritten.Length > original.Length);
        Assert.Equal(0, rewritten.Length % 4);

        var allowedPrefixChanges = Enumerable.Range(selectedRecord + 8, 8).ToHashSet();
        for (int i = 0; i < original.Length; i++)
        {
            if (!allowedPrefixChanges.Contains(i))
                Assert.Equal(original[i], rewritten[i]);
        }

        byte[] expectedPath = Encoding.ASCII.GetBytes(replacement + "\0");
        uint declaredLength = U32(rewritten, selectedRecord + 8);
        uint appendedOffset = U32(rewritten, selectedRecord + 12);
        Assert.Equal((uint)expectedPath.Length, declaredLength);
        Assert.True(appendedOffset >= (uint)Align4(original.Length));
        Assert.Equal(0u, appendedOffset % 4);
        Assert.Equal(expectedPath,
            rewritten.AsSpan(checked((int)appendedOffset), expectedPath.Length).ToArray());

        Assert.Equal(0u, U32(rewritten, selectedRecord));
        Assert.Equal(0xA1u, U32(rewritten, selectedRecord + 4));
        Assert.Equal(4u, U32(rewritten, 0x5C));
        Assert.Equal((uint)TextureTableOffset, U32(rewritten, 0x60));
        Assert.True(RawM2Inspector.RoundTripsExact(rewritten));
    }

    [Fact]
    public void EmptyReplacementSet_IsExactNoOp()
    {
        byte[] source = VanillaTextureFixture();

        byte[] rewritten = M2GeometryPatcher.RewriteHardcodedTexturePaths(source,
            new Dictionary<int, string>());

        Assert.Same(source, rewritten);
    }

    [Fact]
    public void MultipleSlots_AreWrittenInTextureIndexOrderDeterministically()
    {
        byte[] source = VanillaTextureFixture();
        string first = WeaponNaming.EffectTextureMpqPath(99, 1);
        string second = WeaponNaming.EffectTextureMpqPath(99, 2);

        byte[] reverseInput = M2GeometryPatcher.RewriteHardcodedTexturePaths(source,
            new Dictionary<int, string> { [3] = second, [1] = first });
        byte[] forwardInput = M2GeometryPatcher.RewriteHardcodedTexturePaths(source,
            new Dictionary<int, string> { [1] = first, [3] = second });

        Assert.Equal(forwardInput, reverseInput);
        uint firstOffset = U32(forwardInput, TextureTableOffset + 16 + 12);
        uint secondOffset = U32(forwardInput, TextureTableOffset + 48 + 12);
        Assert.True(firstOffset < secondOffset);
        Assert.Equal(0u, firstOffset % 4);
        Assert.Equal(0u, secondOffset % 4);
        Assert.Equal(first, ReadDeclaredPath(forwardInput, TextureTableOffset + 16));
        Assert.Equal(second, ReadDeclaredPath(forwardInput, TextureTableOffset + 48));
    }

    [Theory]
    [InlineData(0)] // Type 2: DBC-driven object skin
    [InlineData(2)] // Type 3: client-provided weapon-blade sheen
    public void NonType0Selection_IsRejectedWithoutMutatingSource(int textureIndex)
    {
        byte[] source = VanillaTextureFixture();
        byte[] original = (byte[])source.Clone();

        var error = Assert.Throws<InvalidOperationException>(() =>
            M2GeometryPatcher.RewriteHardcodedTexturePaths(source,
                new Dictionary<int, string>
                {
                    [textureIndex] = WeaponNaming.EffectTextureMpqPath(7, 1),
                }));

        Assert.Contains("only hardcoded Type-0", error.Message);
        Assert.Equal(original, source);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(-1)]
    public void OutOfRangeSelection_IsRejected(int textureIndex)
    {
        byte[] source = VanillaTextureFixture();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            M2GeometryPatcher.RewriteHardcodedTexturePaths(source,
                new Dictionary<int, string> { [textureIndex] = @"Item\Effect.blp" }));
    }

    [Fact]
    public void NonAsciiOrNulReplacementPath_IsRejected()
    {
        byte[] source = VanillaTextureFixture();
        string[] invalidPaths = ["", "Item\\Bad" + '\0' + "Path.blp", "Item\\EffΩct.blp"];

        foreach (string path in invalidPaths)
        {
            Assert.Throws<ArgumentException>(() =>
                M2GeometryPatcher.RewriteHardcodedTexturePaths(source,
                    new Dictionary<int, string> { [1] = path }));
        }
    }

    [Fact]
    public void MalformedSourceFilenameRange_IsRejected()
    {
        byte[] source = VanillaTextureFixture();
        W32(source, TextureTableOffset + 16 + 12, checked((uint)source.Length - 1));

        Assert.Throws<InvalidOperationException>(() =>
            M2GeometryPatcher.RewriteHardcodedTexturePaths(source,
                new Dictionary<int, string> { [1] = @"Item\Effect.blp" }));
    }

    [Fact]
    public void ExpectedSourcePath_AcceptsEquivalentSlashCaseAndWhitespace()
    {
        byte[] source = VanillaTextureFixture();
        string replacement = WeaponNaming.EffectTextureMpqPath(77, 1);

        byte[] rewritten = M2GeometryPatcher.RewriteHardcodedTexturePaths(
            source,
            new Dictionary<int, string> { [1] = replacement },
            new Dictionary<int, string> { [1] = "  spells/oldglow.BLP  " });

        Assert.Equal(replacement, ReadDeclaredPath(rewritten, TextureTableOffset + 16));
    }

    [Fact]
    public void ExpectedSourcePathMismatch_IsRejectedWithoutMutatingSource()
    {
        byte[] source = VanillaTextureFixture();
        byte[] original = (byte[])source.Clone();

        var error = Assert.Throws<InvalidOperationException>(() =>
            M2GeometryPatcher.RewriteHardcodedTexturePaths(
                source,
                new Dictionary<int, string>
                {
                    [1] = WeaponNaming.EffectTextureMpqPath(77, 1),
                },
                new Dictionary<int, string> { [1] = @"Spells\DifferentGlow.blp" }));

        Assert.Contains("wrong BLP", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, source);
    }

    [Fact]
    public void ExpectedSourcePaths_MustCoverExactlyTheReplacementSlots()
    {
        byte[] source = VanillaTextureFixture();

        Assert.Throws<ArgumentException>(() =>
            M2GeometryPatcher.RewriteHardcodedTexturePaths(
                source,
                new Dictionary<int, string>
                {
                    [1] = WeaponNaming.EffectTextureMpqPath(77, 1),
                },
                new Dictionary<int, string>()));
    }

    private static byte[] VanillaTextureFixture()
    {
        // Deliberately non-aligned EOF exercises the append padding. All zeroed header fields not
        // set below are empty M2Arrays; the patterned tail stands in for animation/track payload
        // whose bytes must survive the repoint untouched.
        var bytes = new byte[0x1EF];
        Encoding.ASCII.GetBytes("MD20").CopyTo(bytes, 0);
        W32(bytes, 0x04, 256);
        W32(bytes, 0x5C, 4);
        W32(bytes, 0x60, TextureTableOffset);

        PutTexture(bytes, 0, type: 2, flags: 0x21, path: null);
        PutTexture(bytes, 1, type: 0, flags: 0xA1, path: @"Spells\OldGlow.blp", pathOffset: 0x1A0);
        PutTexture(bytes, 2, type: 3, flags: 0xA2, path: null);
        PutTexture(bytes, 3, type: 0, flags: 0xA3, path: @"Item\OldEdge.blp", pathOffset: 0x1C0);

        for (int i = 0x1D8; i < bytes.Length; i++)
            bytes[i] = unchecked((byte)(i * 37 + 11));
        return bytes;
    }

    private static void PutTexture(byte[] bytes, int index, uint type, uint flags,
        string? path, int pathOffset = 0)
    {
        int record = TextureTableOffset + index * 16;
        W32(bytes, record, type);
        W32(bytes, record + 4, flags);
        if (path is null) return;

        byte[] encoded = Encoding.ASCII.GetBytes(path + "\0");
        encoded.CopyTo(bytes, pathOffset);
        W32(bytes, record + 8, checked((uint)encoded.Length));
        W32(bytes, record + 12, checked((uint)pathOffset));
    }

    private static string ReadDeclaredPath(byte[] bytes, int record)
    {
        int length = checked((int)U32(bytes, record + 8));
        int offset = checked((int)U32(bytes, record + 12));
        return Encoding.ASCII.GetString(bytes, offset, length - 1);
    }

    private static int Align4(int value) => (value + 3) & ~3;
    private static uint U32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static void W32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
}
