using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

var root = Path.Combine(Path.GetTempPath(), $"mangos-superui-worldstate-{Guid.NewGuid():N}");
try
{
    await RunAsync(root);
    Console.WriteLine("World-state clinical check passed.");
}
finally
{
    if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
}

static async Task RunAsync(string root)
{
    var source = Path.Combine(root, "source");
    var staging = Path.Combine(root, "staging");
    var webRoot = Path.Combine(root, "wwwroot");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(Path.Combine(webRoot, "data"));

    await WriteGzipAsync(Path.Combine(source, WorldArtifactService.WorldMangos),
        SelfContainedDump("mangos", "CREATE TABLE `world_fixture` (`id` INT);"));
    await WriteGzipAsync(Path.Combine(source, WorldArtifactService.WorldAdmin),
        SelfContainedDump("vmangos_admin", "INSERT INTO `bot_registry` (`guid`) VALUES (7);"));
    await WriteGzipAsync(Path.Combine(source, WorldArtifactService.PlayersCharacters),
        SelfContainedDump("characters", "INSERT INTO `characters` (`guid`) VALUES (99);"));
    await WriteGzipAsync(Path.Combine(source, WorldArtifactService.PlayersRealmd),
        SelfContainedDump("realmd", "UPDATE `realmcharacters` SET `numchars`=99 WHERE `realmid`=1;"));
    await WriteGzipAsync(Path.Combine(source, WorldArtifactService.PlayersCharactersSchema),
        "-- character schema fixture\nCREATE TABLE `characters` (`guid` INT);\n");
    await WriteGzipAsync(Path.Combine(source, WorldArtifactService.PlayersCharactersSystem),
        "-- system rows fixture\nINSERT INTO `migrations` (`id`) VALUES ('fixture');\n");
    await WriteTarGzipAsync(Path.Combine(source, WorldArtifactService.CoreArchive),
        new TarFixture(TarEntryType.Directory, "src/", ""),
        new TarFixture(TarEntryType.RegularFile, "src/fixture.txt", "core fixture"),
        new TarFixture(TarEntryType.Directory, "sql/", ""),
        new TarFixture(TarEntryType.RegularFile, "sql/fixture.sql", "SELECT 1;"));
    await File.WriteAllTextAsync(Path.Combine(source, WorldArtifactService.CoreConfig),
        "# fixture\nRealmID = 1\nPlayerLimit = 10\nPlayerHardLimit = 20\nLoginPerTick = 1\n", new UTF8Encoding(false));
    await File.WriteAllTextAsync(Path.Combine(webRoot, "data", "wow_era_5000_names.txt"),
        "Arthas\nJaina\nThrall\nSylvanas\nAnduin\nJAINA\nX\nBad-Name\n", new UTF8Encoding(false));

    var artifacts = new WorldArtifactService();
    var builder = new RtsWorldCreationService(artifacts, new FixtureWebHostEnvironment(root, webRoot));
    var request = new CreateRtsWorldRequestModel
    {
        Name = "Clinical RTS",
        Configuration = new WorldLaunchConfiguration
        {
            ProfileId = WorldConfigurationCatalog.RtsR2ProfileId,
            RealmId = 1,
            PlayerLimit = 2500,
            PlayerHardLimit = 2600,
            LoginPerTick = 7,
            AllianceBotCap = 2,
            HordeBotCap = 2,
            StateFlushMs = 45000
        }
    };

    var result = await builder.BuildAsync(source, staging, request);
    Require(result.NamePoolEligible == 5, "name-list filtering/deduplication changed");
    Require(result.NamePoolSha256.Length == 64, "name-list SHA-256 was not recorded");
    Require(result.Configuration.ProfileId == WorldConfigurationCatalog.RtsR2ProfileId,
        "R2 profile identity was not preserved");

    var config = await MangosdConfigDocument.LoadAsync(
        Path.Combine(staging, WorldArtifactService.CoreConfig));
    Require(config.GetInt("PlayerLimit") == 2500, "PlayerLimit was not staged");
    Require(config.GetInt("PlayerHardLimit") == 2600, "PlayerHardLimit was not staged");
    Require(config.GetInt("LoginPerTick") == 7, "LoginPerTick was not staged");

    var characters = await ReadGzipAsync(Path.Combine(staging, WorldArtifactService.PlayersCharacters));
    Require(characters.Contains("CREATE DATABASE", StringComparison.Ordinal) &&
            characters.Contains("DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci", StringComparison.Ordinal) &&
            characters.Contains("USE `characters`;", StringComparison.Ordinal),
        "generated character artifact did not preserve the source schema charset/collation preamble");
    Require(characters.Contains("CREATE TABLE `characters`", StringComparison.Ordinal),
        "clean character schema was not composed");
    Require(characters.Contains("INSERT INTO `migrations`", StringComparison.Ordinal),
        "character system rows were not composed");
    Require(characters.Contains("('mode','rts')", StringComparison.Ordinal), "RTS mode seed is absent");
    Require(characters.Contains("('bots.cap.alliance','2')", StringComparison.Ordinal),
        "alliance cap seed is absent");
    Require(characters.Contains("('state.flush_ms','45000')", StringComparison.Ordinal),
        "state flush seed is absent");
    Require(characters.Contains("('honor.weight.player','10')", StringComparison.Ordinal) &&
            characters.Contains("('honor.weight.bot','5')", StringComparison.Ordinal) &&
            characters.Contains("('honor.weight.npc','1')", StringComparison.Ordinal) &&
            characters.Contains("('honor.weight.npc_elite','3')", StringComparison.Ordinal),
        "R2 Honor defaults are absent");
    Require(characters.Contains("('honor.suppress_bot_hk','1')", StringComparison.Ordinal) &&
            characters.Contains("('control.faction_bots','1')", StringComparison.Ordinal) &&
            characters.Contains("('honor.enabled','1')", StringComparison.Ordinal) &&
            characters.Contains("('hero.enabled','1')", StringComparison.Ordinal) &&
            characters.Contains("('hero.slots_fixed','4')", StringComparison.Ordinal),
        "R2 boot gates, faction control, suppression, or slot defaults are absent");
    Require(characters.Contains("VALUES (1,20,10,51001,120,120);", StringComparison.Ordinal) &&
            characters.Contains("VALUES (5,320,160,51005,200,200);", StringComparison.Ordinal),
        "R2 hero target-level rows are absent");
    Require(characters.Contains("DELETE FROM `superui_heroes`", StringComparison.Ordinal),
        "campaign state reset is absent");
    Require(!characters.Contains("INSERT INTO `characters`", StringComparison.Ordinal),
        "generated character artifact unexpectedly seeds a player/bot roster");

    var sourceWorld = await ReadGzipAsync(Path.Combine(source, WorldArtifactService.WorldMangos));
    var stagedWorld = await ReadGzipAsync(Path.Combine(staging, WorldArtifactService.WorldMangos));
    Require(!sourceWorld.Contains(RtsHeroSpellWorldStore.OriginalTable, StringComparison.Ordinal),
        "RTS build changed the source world artifact");
    Require(stagedWorld.Contains("0x80000040", StringComparison.Ordinal) &&
            stagedWorld.Contains("`effectApplyAuraName1`,`effectApplyAuraName2`", StringComparison.Ordinal) &&
            stagedWorld.Contains("61,79,0,127", StringComparison.Ordinal),
        "R2 native passive scale/damage aura mechanics are absent from the staged world artifact");
    Require(stagedWorld.Contains("(51001,5875", StringComparison.Ordinal) &&
            stagedWorld.Contains(",19,19,1,1,0,0,61,79", StringComparison.Ordinal) &&
            stagedWorld.Contains("(51005,5875", StringComparison.Ordinal) &&
            stagedWorld.Contains(",99,99,1,1,0,0,61,79", StringComparison.Ordinal),
        "R2 level-one or level-five aura totals are incorrect");
    Require(stagedWorld.Split("INSERT INTO `spell_template`", StringSplitOptions.None).Length - 1 == 5 &&
            !stagedWorld.Contains("(51006,5875", StringComparison.Ordinal),
        "R2 world artifact updates more than the five reserved spell rows");
    Require(stagedWorld.Contains($"CREATE TABLE IF NOT EXISTS `{RtsHeroSpellWorldStore.OriginalTable}` LIKE `spell_template`", StringComparison.Ordinal) &&
            stagedWorld.Contains("NOT EXISTS", StringComparison.Ordinal),
        "pre-R2 spell rows are not preserved once for profile rollback");

    var admin = await ReadGzipAsync(Path.Combine(staging, WorldArtifactService.WorldAdmin));
    Require(admin.Contains("DELETE FROM `bot_registry`", StringComparison.Ordinal),
        "admin campaign-state reset is absent");
    var realmd = await ReadGzipAsync(Path.Combine(staging, WorldArtifactService.PlayersRealmd));
    Require(realmd.Contains("UPDATE `realmcharacters` SET `numchars`=0;", StringComparison.Ordinal),
        "realm character count reset is absent");
    Require((await File.ReadAllTextAsync(Path.Combine(staging, WorldArtifactService.RotationAssignments))).Trim() == "{}",
        "rotation assignments are not empty");

    var described = await artifacts.DescribeV2ArtifactsAsync(staging);
    Require(described.Count == WorldArtifactService.V2Artifacts.Length,
        "staged artifact set is incomplete");
    Require(described.All(x => x.Length > 0 && x.Sha256.Length == 64),
        "artifact metadata is incomplete");
    Require(WorldArtifactService.ValidateV2ArtifactMetadata(described, "fixture").Count == 0,
        "valid v2 artifact metadata was rejected");

    var weakened = CloneArtifacts(described);
    var weakenedWorld = weakened.Single(x => x.FileName == WorldArtifactService.WorldMangos);
    weakenedWorld.Required = false;
    weakenedWorld.Length = 0;
    weakenedWorld.Sha256 = "";
    weakenedWorld.Format = "text";
    Require(WorldArtifactService.ValidateV2ArtifactMetadata(weakened, "weakened").Count >= 3,
        "required v2 metadata could be weakened without detection");

    var duplicated = CloneArtifacts(described);
    duplicated.Add(CloneArtifact(duplicated[0]));
    Require(WorldArtifactService.ValidateV2ArtifactMetadata(duplicated, "duplicate")
            .Any(error => error.Contains("duplicate artifact filename", StringComparison.OrdinalIgnoreCase)),
        "duplicate v2 artifact filenames were accepted");

    var wrongCase = CloneArtifacts(described);
    wrongCase.Single(x => x.FileName == WorldArtifactService.WorldMangos).FileName =
        WorldArtifactService.WorldMangos.ToUpperInvariant();
    Require(WorldArtifactService.ValidateV2ArtifactMetadata(wrongCase, "wrong-case")
            .Any(error => error.Contains("canonical", StringComparison.OrdinalIgnoreCase)),
        "non-canonical artifact filename casing was accepted");

    var listedOptional = described.Single(x => x.FileName == WorldArtifactService.RotationAssignments);
    File.Delete(Path.Combine(staging, listedOptional.FileName));
    var missingOptionalCheck = await artifacts.ValidateArtifactAsync(staging, listedOptional);
    Require(!missingOptionalCheck.Valid && !missingOptionalCheck.Present,
        "a manifest-listed optional artifact was accepted after its file disappeared");

    var diskMismatch = CloneArtifacts(described);
    diskMismatch[0].Sha256 = new string('0', 64);
    Require(WorldArtifactService.CompareV2ArtifactMetadata(described, diskMismatch).Count == 1,
        "worlds.json/manifest.json artifact drift was not detected");

    var duplicate = MangosdConfigDocument.Parse("PlayerLimit = 10\nPlayerLimit = 20\n");
    ExpectThrows<InvalidOperationException>(() => duplicate.Get("PlayerLimit"),
        "duplicate active config keys were accepted");
    ExpectThrows<InvalidDataException>(() => WorldArtifactService.ResolveChild(source, "../escape.sql.gz"),
        "artifact path traversal was accepted");

    var traversalArchive = Path.Combine(root, "traversal.tar.gz");
    await WriteTarGzipAsync(traversalArchive,
        new TarFixture(TarEntryType.RegularFile, "safe/../../escape.txt", "escape"));
    await ExpectThrowsAsync<InvalidDataException>(() => artifacts.ValidateTarGzipAsync(traversalArchive),
        "tar path traversal was accepted");

    var linkArchive = Path.Combine(root, "link.tar.gz");
    await WriteTarGzipAsync(linkArchive,
        new TarFixture(TarEntryType.SymbolicLink, "src/link", "../../outside"));
    await ExpectThrowsAsync<InvalidDataException>(() => artifacts.ValidateTarGzipAsync(linkArchive),
        "tar link entry was accepted");

    var databaseDefinition = await artifacts.InspectDatabaseDumpAsync(
        Path.Combine(source, WorldArtifactService.PlayersCharacters), "characters");
    Require(databaseDefinition?.CharacterSet == "utf8mb4" &&
            databaseDefinition.Collation == "utf8mb4_unicode_ci",
        "self-contained database dump charset/collation was not detected");
    Require(WorldArtifactService.BuildDatabasePreamble(databaseDefinition!)
            .Contains("DROP DATABASE IF EXISTS `characters`;", StringComparison.Ordinal),
        "self-contained database preamble is incomplete");
    var legacyDump = Path.Combine(root, "legacy-table-only.sql.gz");
    await WriteGzipAsync(legacyDump, "CREATE TABLE `legacy_fixture` (`id` INT);\n");
    Require(await artifacts.InspectDatabaseDumpAsync(legacyDump, "characters") == null,
        "legacy table-only dump was misclassified as self-contained");

    await RunCoreRestoreChecksAsync(root, artifacts);

    var seed = RtsWorldCreationService.BuildCharactersSeedSql(request.Configuration);
    Require(seed.Contains("('mode','rts')", StringComparison.Ordinal), "standalone seed is not RTS mode");
    Require(seed.Contains("INSERT INTO `superui_faction`", StringComparison.Ordinal),
        "faction genesis seed is absent");
    string[] rtsTables =
    {
        "superui_worldstate", "superui_rules_zone", "superui_rules_hub",
        "superui_rules_hero", "superui_rules_dungeon", "superui_faction",
        "superui_heroes", "superui_zone_control", "superui_dungeon_control"
    };
    foreach (var table in rtsTables)
        Require(seed.Contains($"CREATE TABLE IF NOT EXISTS `{table}`", StringComparison.Ordinal),
            $"RTS creation seed does not create {table}");
    Require(!seed.Contains("ALTER TABLE", StringComparison.OrdinalIgnoreCase),
        "clean R2 creation still contains legacy schema-upgrade DDL");
    Require(seed.Contains("VALUES ('honor.enabled','1')", StringComparison.Ordinal) &&
            seed.Contains("VALUES ('hero.enabled','1')", StringComparison.Ordinal) &&
            seed.Contains("VALUES ('control.faction_bots','1')", StringComparison.Ordinal) &&
            seed.Contains("VALUES (1,20,10,51001,120,120);", StringComparison.Ordinal),
        "the sole RTS profile does not enable its Honor/Hero contract");
    Require(WorldConfigurationCatalog.Profiles.Count == 1 &&
            WorldConfigurationCatalog.Profiles[0].Id == WorldConfigurationCatalog.RtsR2ProfileId,
        "more than one RTS profile remains exposed");
    var rejectedR1 = WorldConfigurationCatalog.CreateDefaults(WorldConfigurationCatalog.RtsR2ProfileId);
    rejectedR1.ProfileId = "rts-r1-v1";
    ExpectThrows<InvalidOperationException>(
        () => WorldConfigurationCatalog.NormalizeAndValidate(rejectedR1),
        "the removed R1 profile was accepted");

    var creationPostlude = RtsHeroSpellWorldStore.BuildCreationArtifactPostlude(request.Configuration);
    Require(creationPostlude.Contains($"CREATE TABLE IF NOT EXISTS `{RtsHeroSpellWorldStore.OriginalTable}`", StringComparison.Ordinal) &&
            creationPostlude.Contains($"CREATE TABLE IF NOT EXISTS `{RtsHeroSpellWorldStore.OriginalStateTable}`", StringComparison.Ordinal) &&
            creationPostlude.Contains("0x80000040", StringComparison.Ordinal),
        "RTS creation does not create preservation tables and install hero aura rows");
    var resumePostlude = RtsHeroSpellWorldStore.BuildResumeArtifactPostlude(request.Configuration);
    Require(!resumePostlude.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase) &&
            !resumePostlude.Contains("ALTER TABLE", StringComparison.OrdinalIgnoreCase) &&
            resumePostlude.Contains("0x80000040", StringComparison.Ordinal),
        "RTS resume performs schema DDL or fails to refresh managed hero aura rows");

    var invalidR2 = WorldConfigurationCatalog.CreateDefaults(WorldConfigurationCatalog.RtsR2ProfileId);
    invalidR2.HeroRules.RemoveAt(4);
    ExpectThrows<InvalidOperationException>(
        () => WorldConfigurationCatalog.NormalizeAndValidate(invalidR2),
        "incomplete R2 hero rules were accepted");
    var remappedR2 = WorldConfigurationCatalog.CreateDefaults(WorldConfigurationCatalog.RtsR2ProfileId);
    remappedR2.HeroRules[0].SpellId = 51009;
    ExpectThrows<InvalidOperationException>(
        () => WorldConfigurationCatalog.NormalizeAndValidate(remappedR2),
        "an R2 hero rule escaped the reserved 51001-51005 spell range");
    var oversizedR2 = WorldConfigurationCatalog.CreateDefaults(WorldConfigurationCatalog.RtsR2ProfileId);
    oversizedR2.HeroRules[0].ScalePercent = 201;
    ExpectThrows<InvalidOperationException>(
        () => WorldConfigurationCatalog.NormalizeAndValidate(oversizedR2),
        "an R2 hero scale above the server's 200 percent ceiling was accepted");
    var maximumHeroSlotsR2 = WorldConfigurationCatalog.CreateDefaults(WorldConfigurationCatalog.RtsR2ProfileId);
    maximumHeroSlotsR2.HeroSlotsFixed = 127;
    Require(WorldConfigurationCatalog.NormalizeAndValidate(maximumHeroSlotsR2).HeroSlotsFixed == 127,
        "the maximum wire-safe R2 hero-slot count was rejected");
    var oversizedHeroSlotsR2 = WorldConfigurationCatalog.CreateDefaults(WorldConfigurationCatalog.RtsR2ProfileId);
    oversizedHeroSlotsR2.HeroSlotsFixed = 128;
    ExpectThrows<InvalidOperationException>(
        () => WorldConfigurationCatalog.NormalizeAndValidate(oversizedHeroSlotsR2),
        "an R2 hero-slot count above the u8 state-packet envelope was accepted");
}

static async Task RunCoreRestoreChecksAsync(string root, WorldArtifactService artifacts)
{
    var coreRoot = Path.Combine(root, "core-restore");
    var sourceRoot = Path.Combine(coreRoot, "src");
    var sqlRoot = Path.Combine(coreRoot, "sql");
    var configTarget = Path.Combine(coreRoot, "run", "mangosd.conf");
    var rotationTarget = Path.Combine(coreRoot, "rotations", "assignments.json");
    Directory.CreateDirectory(sourceRoot);
    Directory.CreateDirectory(sqlRoot);
    Directory.CreateDirectory(Path.GetDirectoryName(configTarget)!);
    Directory.CreateDirectory(Path.GetDirectoryName(rotationTarget)!);
    await File.WriteAllTextAsync(Path.Combine(sourceRoot, "old.txt"), "old source");
    await File.WriteAllTextAsync(Path.Combine(sqlRoot, "old.sql"), "old sql");
    await File.WriteAllTextAsync(configTarget, "PlayerLimit = 1\n");
    await File.WriteAllTextAsync(rotationTarget, "{\"Oldbot\":\"old\"}\n");

    var archive = Path.Combine(coreRoot, "v2.tar.gz");
    await WriteTarGzipAsync(archive,
        new TarFixture(TarEntryType.Directory, "src/", ""),
        new TarFixture(TarEntryType.RegularFile, "src/new.txt", "new source"),
        new TarFixture(TarEntryType.Directory, "sql/", ""),
        new TarFixture(TarEntryType.RegularFile, "sql/new.sql", "new sql"));
    var configSidecar = Path.Combine(coreRoot, "core_mangosd.conf");
    var rotationSidecar = Path.Combine(coreRoot, "bot_rotation_assignments.json");
    await File.WriteAllTextAsync(configSidecar, "PlayerLimit = 2500\nPlayerHardLimit = 2600\n");
    await File.WriteAllTextAsync(rotationSidecar, "{\"Newbot\":\"r1\"}\n");

    var restored = await artifacts.RestoreCoreArtifactsAsync(
        archive, sourceRoot, sqlRoot, configSidecar, configTarget, rotationSidecar, rotationTarget);
    Require(!restored.LegacyConfig && restored.SourceFiles == 1 && restored.SqlFiles == 1,
        "v2 core restore result is incorrect");
    Require(File.Exists(Path.Combine(sourceRoot, "new.txt")) &&
            !File.Exists(Path.Combine(sourceRoot, "old.txt")),
        "source root was overlaid instead of exactly replaced");
    Require(File.Exists(Path.Combine(sqlRoot, "new.sql")) &&
            !File.Exists(Path.Combine(sqlRoot, "old.sql")),
        "SQL root was overlaid instead of exactly replaced");
    Require((await File.ReadAllTextAsync(configTarget)).Contains("PlayerLimit = 2500", StringComparison.Ordinal),
        "exact-path v2 config was not replaced");
    Require((await File.ReadAllTextAsync(rotationTarget)).Contains("Newbot", StringComparison.Ordinal),
        "v2 rotation assignments were not replaced");
    Require(!Directory.EnumerateFileSystemEntries(coreRoot, ".*.worldstate-*", SearchOption.AllDirectories).Any(),
        "successful core restore left staging/backup paths behind");

    var legacyRoot = Path.Combine(root, "legacy-core-restore");
    var legacySource = Path.Combine(legacyRoot, "src");
    var legacySql = Path.Combine(legacyRoot, "sql");
    var legacyConfig = Path.Combine(legacyRoot, "run", "mangosd.conf");
    var legacyRotations = Path.Combine(legacyRoot, "rotations", "assignments.json");
    Directory.CreateDirectory(legacySource);
    Directory.CreateDirectory(legacySql);
    Directory.CreateDirectory(Path.GetDirectoryName(legacyConfig)!);
    await File.WriteAllTextAsync(Path.Combine(legacySource, "old.txt"), "old");
    await File.WriteAllTextAsync(Path.Combine(legacySql, "old.sql"), "old");
    await File.WriteAllTextAsync(legacyConfig, "PlayerLimit = 2\n");
    var legacyArchive = Path.Combine(legacyRoot, "legacy.tar.gz");
    await WriteTarGzipAsync(legacyArchive,
        new TarFixture(TarEntryType.Directory, "src/", ""),
        new TarFixture(TarEntryType.RegularFile, "src/legacy-new.txt", "new"),
        new TarFixture(TarEntryType.Directory, "sql/", ""),
        new TarFixture(TarEntryType.RegularFile, "sql/legacy-new.sql", "new"),
        new TarFixture(TarEntryType.RegularFile, "mangosd.conf", "PlayerLimit = 2400\n"));
    var legacyResult = await artifacts.RestoreCoreArtifactsAsync(
        legacyArchive, legacySource, legacySql, null, legacyConfig, null, legacyRotations);
    Require(legacyResult.LegacyConfig &&
            (await File.ReadAllTextAsync(legacyConfig)).Contains("PlayerLimit = 2400", StringComparison.Ordinal),
        "legacy root config adapter failed");
    Require((await File.ReadAllTextAsync(legacyRotations)).Trim() == "{}",
        "legacy core restore did not reset world-scoped rotations");

    var rollbackRoot = Path.Combine(root, "core-rollback");
    var rollbackSource = Path.Combine(rollbackRoot, "src");
    var rollbackSql = Path.Combine(rollbackRoot, "sql");
    var rollbackConfig = Path.Combine(rollbackRoot, "run", "mangosd.conf");
    var invalidRotationTarget = Path.Combine(rollbackRoot, "rotation-target-is-directory");
    Directory.CreateDirectory(rollbackSource);
    Directory.CreateDirectory(rollbackSql);
    Directory.CreateDirectory(Path.GetDirectoryName(rollbackConfig)!);
    Directory.CreateDirectory(invalidRotationTarget);
    await File.WriteAllTextAsync(Path.Combine(rollbackSource, "original.txt"), "source original");
    await File.WriteAllTextAsync(Path.Combine(rollbackSql, "original.sql"), "sql original");
    await File.WriteAllTextAsync(rollbackConfig, "PlayerLimit = 77\n");
    var rollbackArchive = Path.Combine(rollbackRoot, "replacement.tar.gz");
    await WriteTarGzipAsync(rollbackArchive,
        new TarFixture(TarEntryType.Directory, "src/", ""),
        new TarFixture(TarEntryType.RegularFile, "src/replacement.txt", "replacement"),
        new TarFixture(TarEntryType.Directory, "sql/", ""),
        new TarFixture(TarEntryType.RegularFile, "sql/replacement.sql", "replacement"));
    var rollbackConfigSidecar = Path.Combine(rollbackRoot, "replacement.conf");
    await File.WriteAllTextAsync(rollbackConfigSidecar, "PlayerLimit = 88\n");
    await ExpectThrowsAsync<InvalidOperationException>(() => artifacts.RestoreCoreArtifactsAsync(
            rollbackArchive, rollbackSource, rollbackSql, rollbackConfigSidecar, rollbackConfig,
            null, invalidRotationTarget),
        "late core replacement failure was not surfaced");
    Require(File.Exists(Path.Combine(rollbackSource, "original.txt")) &&
            !File.Exists(Path.Combine(rollbackSource, "replacement.txt")) &&
            File.Exists(Path.Combine(rollbackSql, "original.sql")) &&
            !File.Exists(Path.Combine(rollbackSql, "replacement.sql")) &&
            (await File.ReadAllTextAsync(rollbackConfig)).Contains("PlayerLimit = 77", StringComparison.Ordinal),
        "late core replacement failure did not roll back the whole core group");
}

static string SelfContainedDump(string database, string body) =>
    $"-- self-contained fixture\n" +
    $"DROP DATABASE IF EXISTS `{database}`;\n" +
    $"CREATE DATABASE /*!32312 IF NOT EXISTS*/ `{database}` /*!40100 DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci */;\n" +
    $"USE `{database}`;\n{body}\n";

static async Task WriteGzipAsync(string path, string text)
{
    await using var file = File.Create(path);
    await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
    await gzip.WriteAsync(Encoding.UTF8.GetBytes(text));
}

static async Task<string> ReadGzipAsync(string path)
{
    await using var file = File.OpenRead(path);
    await using var gzip = new GZipStream(file, CompressionMode.Decompress);
    using var reader = new StreamReader(gzip, Encoding.UTF8);
    return await reader.ReadToEndAsync();
}

static async Task WriteTarGzipAsync(string path, params TarFixture[] fixtures)
{
    await using var file = File.Create(path);
    await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
    using var writer = new TarWriter(gzip);
    foreach (var fixture in fixtures)
    {
        var entry = new PaxTarEntry(fixture.EntryType, fixture.EntryName);
        if (fixture.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
            entry.LinkName = fixture.ContentOrLinkTarget;
        else if (fixture.EntryType != TarEntryType.Directory)
            entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(fixture.ContentOrLinkTarget));
        writer.WriteEntry(entry);
    }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static List<SnapshotArtifact> CloneArtifacts(IEnumerable<SnapshotArtifact> artifacts) =>
    artifacts.Select(CloneArtifact).ToList();

static SnapshotArtifact CloneArtifact(SnapshotArtifact artifact) => new()
{
    Id = artifact.Id,
    Group = artifact.Group,
    FileName = artifact.FileName,
    Format = artifact.Format,
    Length = artifact.Length,
    Sha256 = artifact.Sha256,
    Required = artifact.Required
};

static void ExpectThrows<TException>(Action action, string message) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

static async Task ExpectThrowsAsync<TException>(Func<Task> action, string message) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

file sealed class FixtureWebHostEnvironment : IWebHostEnvironment
{
    public FixtureWebHostEnvironment(string contentRootPath, string webRootPath)
    {
        ContentRootPath = contentRootPath;
        WebRootPath = webRootPath;
        ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
        WebRootFileProvider = new PhysicalFileProvider(webRootPath);
    }

    public string ApplicationName { get; set; } = "worldstate-clinical-check";
    public string EnvironmentName { get; set; } = "Clinical";
    public string WebRootPath { get; set; }
    public IFileProvider WebRootFileProvider { get; set; }
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }
}

file sealed record TarFixture(
    TarEntryType EntryType,
    string EntryName,
    string ContentOrLinkTarget);
