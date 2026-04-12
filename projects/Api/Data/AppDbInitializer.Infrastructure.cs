using System.Security.Cryptography;
using System.Text;
using Api.Data.Entities;
using Api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public sealed partial class AppDbInitializer
{
    private async Task SeedBuildingLotsAsync()
    {
        var bratislava = await dbContext.Cities.FirstAsync(c => c.Name == "Bratislava");
        var resources = await dbContext.ResourceTypes.ToDictionaryAsync(r => r.Slug);

        // Bratislava building lots across different districts.
        // Coordinates are spread around the city center (48.1486, 17.1077).
        //
        // BasePrice is the pure land anchor value (no resource premium).
        // LandService.RefreshLandState is called below to compute the dynamic PopulationIndex
        // and the final Price = ComputeAppraisedPrice(basePrice, populationIndex) + resourcePremium.
        // This means mine lots with raw-material deposits will always have Price > BasePrice.
        var lotsToSeed = new List<BuildingLot>
        {
            // ── Industrial Zone (eastern outskirts) ──
            // Low population index: these lots are near logistics hubs but away from residential areas.
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-industrial-1"),
                CityId = bratislava.Id,
                Name = "Industrial Plot A1",
                Description = "Large industrial plot near the eastern logistics corridor. Sits above an Iron Ore deposit (18,000t at 72% quality).",
                District = "Industrial Zone",
                Latitude = 48.1520, Longitude = 17.1250,
                PopulationIndex = 0.65m,
                BasePrice = 75_000m,
                Price = 75_000m,  // will be recomputed below
                SuitableTypes = "FACTORY,MINE",
                ResourceTypeId = resources.TryGetValue("iron-ore", out var ironOre) ? ironOre.Id : null,
                ResourceType = resources.TryGetValue("iron-ore", out var ironOreNav) ? ironOreNav : null,
                MaterialQuality = 0.72m,
                MaterialQuantity = 18_000m
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-industrial-2"),
                CityId = bratislava.Id,
                Name = "Industrial Plot A2",
                Description = "Adjacent to major rail freight terminal. Sits above a Chemical Minerals deposit (12,000t at 55% quality).",
                District = "Industrial Zone",
                Latitude = 48.1540, Longitude = 17.1280,
                PopulationIndex = 0.60m,
                BasePrice = 65_000m,
                Price = 65_000m,  // will be recomputed below
                SuitableTypes = "FACTORY,MINE",
                ResourceTypeId = resources.TryGetValue("chemical-minerals", out var chem) ? chem.Id : null,
                ResourceType = resources.TryGetValue("chemical-minerals", out var chemNav) ? chemNav : null,
                MaterialQuality = 0.55m,
                MaterialQuantity = 12_000m
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-industrial-3"),
                CityId = bratislava.Id,
                Name = "Factory Site B1",
                Description = "Modern industrial park with good power grid access. Suitable for energy-intensive production.",
                District = "Industrial Zone",
                Latitude = 48.1500, Longitude = 17.1300,
                PopulationIndex = 0.72m,
                BasePrice = 90_000m,
                Price = 90_000m,
                SuitableTypes = "FACTORY,POWER_PLANT"
            },
            // ── Commercial District (city center) ──
            // High population index: these lots are in the heart of the city with dense foot traffic.
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-commercial-1"),
                CityId = bratislava.Id,
                Name = "High Street Retail Space",
                Description = "Prime storefront on the main pedestrian avenue. High foot traffic and visibility.",
                District = "Commercial District",
                Latitude = 48.1450, Longitude = 17.1070,
                PopulationIndex = 1.85m,
                BasePrice = 120_000m,
                Price = 120_000m,
                SuitableTypes = "SALES_SHOP,COMMERCIAL"
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-commercial-2"),
                CityId = bratislava.Id,
                Name = "Market Square Shop",
                Description = "Corner lot facing the historic market square. Excellent for retail with tourist exposure.",
                District = "Commercial District",
                Latitude = 48.1440, Longitude = 17.1090,
                PopulationIndex = 2.10m,
                BasePrice = 150_000m,
                Price = 150_000m,
                SuitableTypes = "SALES_SHOP,COMMERCIAL"
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-commercial-3"),
                CityId = bratislava.Id,
                Name = "Shopping Boulevard Unit",
                Description = "Mid-range retail space on a busy commercial boulevard with steady local traffic.",
                District = "Commercial District",
                Latitude = 48.1460, Longitude = 17.1050,
                PopulationIndex = 1.60m,
                BasePrice = 100_000m,
                Price = 100_000m,
                SuitableTypes = "SALES_SHOP"
            },
            // ── Business Park (northern area) ──
            // Moderate-to-high population index: professional district with daytime footfall.
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-business-1"),
                CityId = bratislava.Id,
                Name = "Innovation Campus Office",
                Description = "Modern office complex in the technology business park. Perfect for R&D operations.",
                District = "Business Park",
                Latitude = 48.1560, Longitude = 17.1100,
                PopulationIndex = 1.20m,
                BasePrice = 130_000m,
                Price = 130_000m,
                SuitableTypes = "RESEARCH_DEVELOPMENT,BANK"
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-business-2"),
                CityId = bratislava.Id,
                Name = "Financial Center Suite",
                Description = "Premium office space in the financial district. Ideal for banking and exchange operations.",
                District = "Business Park",
                Latitude = 48.1570, Longitude = 17.1060,
                PopulationIndex = 1.40m,
                BasePrice = 200_000m,
                Price = 200_000m,
                SuitableTypes = "BANK,EXCHANGE"
            },
            // ── Residential Quarter (western area) ──
            // Steady population index: consistent local demand from residents.
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-residential-1"),
                CityId = bratislava.Id,
                Name = "Riverside Apartment Block",
                Description = "Scenic residential plot overlooking the Danube. Strong rental demand from young professionals.",
                District = "Residential Quarter",
                Latitude = 48.1400, Longitude = 17.1000,
                PopulationIndex = 1.05m,
                BasePrice = 110_000m,
                Price = 110_000m,
                SuitableTypes = "APARTMENT"
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-residential-2"),
                CityId = bratislava.Id,
                Name = "Suburban Housing Site",
                Description = "Affordable residential lot in a growing suburban neighborhood. Good long-term rental potential.",
                District = "Residential Quarter",
                Latitude = 48.1380, Longitude = 17.0950,
                PopulationIndex = 0.88m,
                BasePrice = 70_000m,
                Price = 70_000m,
                SuitableTypes = "APARTMENT"
            },
            // ── Media & Cultural District (south-central) ──
            // Moderate population index: near cultural venues with evening and weekend activity.
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-media-1"),
                CityId = bratislava.Id,
                Name = "Broadcast Tower Complex",
                Description = "Purpose-built media complex near the cultural center. Ideal for newspaper, radio, or TV operations.",
                District = "Media District",
                Latitude = 48.1420, Longitude = 17.1120,
                PopulationIndex = 1.25m,
                BasePrice = 140_000m,
                Price = 140_000m,
                SuitableTypes = "MEDIA_HOUSE"
            },
            // ── Energy Zone (south-eastern outskirts) ──
            // Low population index: far from residential areas; access to grid infrastructure.
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-energy-1"),
                CityId = bratislava.Id,
                Name = "Power Generation Site",
                Description = "Large plot with grid connection capacity for power generation. Zoned for energy infrastructure.",
                District = "Energy Zone",
                Latitude = 48.1350, Longitude = 17.1200,
                PopulationIndex = 0.52m,
                BasePrice = 160_000m,
                Price = 160_000m,
                SuitableTypes = "POWER_PLANT"
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-energy-2"),
                CityId = bratislava.Id,
                Name = "Utility Substation Plot",
                Description = "Secondary energy plot suitable for smaller power plants or supplementary generation.",
                District = "Energy Zone",
                Latitude = 48.1360, Longitude = 17.1230,
                PopulationIndex = 0.55m,
                BasePrice = 100_000m,
                Price = 100_000m,
                SuitableTypes = "POWER_PLANT,FACTORY"
            }
        };

        dbContext.BuildingLots.AddRange(lotsToSeed);

        // Apply resource premium: Price = appraised land value + resource deposit premium.
        // This runs in the seeder so every fresh database starts with correct prices.
        // The tick engine recalculates prices on every tick using the same formula.
        foreach (var lot in lotsToSeed)
        {
            var resourcePremium = LandService.ComputeResourcePremium(
                lot.ResourceType, lot.MaterialQuality, lot.MaterialQuantity);
            if (resourcePremium > 0m)
            {
                var appraisedLandValue = LandService.ComputeAppraisedPrice(lot.BasePrice, lot.PopulationIndex);
                lot.Price = appraisedLandValue + resourcePremium;
            }
        }
    }

    private static Guid CreateDeterministicGuid(string key)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }

    /// <summary>
    /// Applies the database schema in a way that is safe for three distinct startup scenarios:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>In-memory database (local development)</b> — EF migrations are not supported by
    ///     the in-memory provider.  <c>EnsureCreatedAsync</c> is used directly; migration steps
    ///     are skipped.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Fresh relational database</b> — <c>EnsureCreatedAsync</c> creates the schema,
    ///     then a baseline entry is inserted into <c>__EFMigrationsHistory</c> for every
    ///     currently-defined migration so that <c>MigrateAsync</c> has nothing left to apply.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Legacy relational database</b> (created by <c>EnsureCreatedAsync</c> before
    ///     migration support was introduced, no <c>__EFMigrationsHistory</c> table) —
    ///     same as fresh path: baseline entries are inserted for all existing migrations so that
    ///     the initial migration is not replayed against tables that already exist.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Relational database already managed by migrations</b> — <c>EnsureCreatedAsync</c>
    ///     is a no-op, <c>EnsureMigrationsHistoryBaselineAsync</c> detects the existing history
    ///     table and returns immediately, and <c>MigrateAsync</c> applies only pending
    ///     migrations.
    ///   </description></item>
    /// </list>
    /// </summary>
    private async Task SafelyApplyMigrationsAsync()
    {
        // In-memory databases (development mode: UseInMemoryDatabase) do not support EF
        // migrations at all — the in-memory provider has no IMigrator service.  Fall back to
        // EnsureCreatedAsync which correctly builds the schema from the current model.
        if (!dbContext.Database.IsRelational())
        {
            await dbContext.Database.EnsureCreatedAsync();
            return;
        }

        // For relational databases (SQLite in production and test environments):
        //   a) EnsureCreatedAsync is idempotent and safe to call even when tables already exist.
        //      It creates the database file and schema when absent and returns false (no-op)
        //      when the database already exists — regardless of whether it was originally
        //      created by EnsureCreated or by a previous migration run.
        await dbContext.Database.EnsureCreatedAsync();

        //   b) If the database has no __EFMigrationsHistory table, every currently-defined
        //      migration is marked as already applied so that MigrateAsync (step c) does not
        //      attempt to re-create tables that already exist.
        var baselinedMigrationsHistory = await EnsureMigrationsHistoryBaselineAsync();

        if (baselinedMigrationsHistory)
        {
            return;
        }

        //   c) Apply any migrations that are not yet recorded in __EFMigrationsHistory.
        //      This is a no-op for brand-new or already up-to-date databases.
        try
        {
            await dbContext.Database.MigrateAsync();
        }
        catch (Exception exc)
        {
            Console.Error.WriteLine($"An error occurred while applying migrations: {exc.Message}");
        }
    }

    /// <summary>
    /// Creates <c>__EFMigrationsHistory</c> and inserts baseline rows for every currently-defined
    /// migration when the table does not exist.  This makes databases that were bootstrapped
    /// with <c>EnsureCreatedAsync</c> (before migration support was introduced) compatible with
    /// <c>MigrateAsync</c> without requiring a database drop-and-recreate.
    ///
    /// If <c>__EFMigrationsHistory</c> already exists the method returns immediately; it never
    /// removes or alters existing history rows.
    /// </summary>
    private async Task<bool> EnsureMigrationsHistoryBaselineAsync()
    {
        // The EF Core migration history table name matches the default used by
        // SqliteHistoryRepository (and other relational providers).
        const string historyTable = "__EFMigrationsHistory";

        // The ProductVersion column records which EF Core version managed each migration.
        // For baseline rows we record the current EF Core runtime version so reviewers can
        // see when the baseline was applied.
        var efProductVersion =
            (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly))
            ?.InformationalVersion
            ?? "unknown";

        var connection = dbContext.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;

        if (!wasOpen)
            await connection.OpenAsync();

        try
        {
            // Check whether the migrations-history table already exists.
            await using var checkCmd = connection.CreateCommand();
            var provider = dbContext.Database.ProviderName ?? "Unknown";
            string checkQuery;
            if (provider.Contains("PostgreSQL"))
            {
                checkQuery = $"SELECT COUNT(1) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind = 'r' AND n.nspname = 'public' AND c.relname = '{historyTable}'";
            }
            else
            {
                checkQuery = $"SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='{historyTable}'";
            }
            checkCmd.CommandText = checkQuery;
            var historyExists =
                Convert.ToInt64(await checkCmd.ExecuteScalarAsync() ?? 0L) > 0;

            if (historyExists)
                return false; // Already managed by migrations — nothing to do.

            // Create the history table using the SQLite-compatible schema that EF Core generates.
            await using var createCmd = connection.CreateCommand();
            createCmd.CommandText =
                $"""
                CREATE TABLE "{historyTable}" (
                    "MigrationId" TEXT NOT NULL,
                    "ProductVersion" TEXT NOT NULL,
                    CONSTRAINT "PK__{historyTable}" PRIMARY KEY ("MigrationId")
                )
                """;
            await createCmd.ExecuteNonQueryAsync();

            // Baseline: mark every currently-defined migration as already applied.
            // EnsureCreatedAsync (called just before this) already created the full schema
            // corresponding to all migrations up to HEAD, so no migration needs to be
            // replayed.  Future migrations added after this baseline will still be applied
            // correctly by MigrateAsync because they will NOT be in this initial history.
            foreach (var migrationId in dbContext.Database.GetMigrations())
            {
                // Use parameterized queries to prevent any injection risks even though
                // migration IDs come from the compiled assembly (not external input).
                await using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText =
                    $"""
                    INSERT INTO "{historyTable}" ("MigrationId", "ProductVersion")
                    VALUES (@MigrationId, @ProductVersion)
                    """;
                var migParam = insertCmd.CreateParameter();
                migParam.ParameterName = "@MigrationId";
                migParam.Value = migrationId;
                insertCmd.Parameters.Add(migParam);

                var verParam = insertCmd.CreateParameter();
                verParam.ParameterName = "@ProductVersion";
                verParam.Value = efProductVersion;
                insertCmd.Parameters.Add(verParam);

                await insertCmd.ExecuteNonQueryAsync();
            }

            return true;
        }
        finally
        {
            if (!wasOpen)
                connection.Close();
        }
    }
}
