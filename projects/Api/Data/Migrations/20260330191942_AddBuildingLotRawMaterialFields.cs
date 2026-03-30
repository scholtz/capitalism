using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildingLotRawMaterialFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    Population = table.Column<int>(type: "INTEGER", nullable: false),
                    AverageRentPerSqm = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    BaseSalaryPerManhour = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CurrentTick = table.Column<long>(type: "INTEGER", nullable: false),
                    LastTickAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TickIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    TaxCycleTicks = table.Column<int>(type: "INTEGER", nullable: false),
                    TaxRate = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OnboardingCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProSubscriptionEndsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "TEXT", nullable: false),
                    OnboardingCurrentStep = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    OnboardingIndustry = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    OnboardingCityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OnboardingCompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OnboardingFactoryLotId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OnboardingShopBuildingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OnboardingFirstSaleCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Industry = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    BasePrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    BaseCraftTicks = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    EnergyConsumptionMwh = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    BasicLaborHours = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    IsProOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    UnitName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UnitSymbol = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    BasePrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    WeightPerUnit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UnitSymbol = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 12000, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Cash = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    FoundedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FoundedAtTick = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Companies_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CityResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Abundance = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CityResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CityResources_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CityResources_ResourceTypes_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalTable: "ResourceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductRecipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InputProductTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductRecipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductRecipes_ProductTypes_InputProductTypeId",
                        column: x => x.InputProductTypeId,
                        principalTable: "ProductTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductRecipes_ProductTypes_ProductTypeId",
                        column: x => x.ProductTypeId,
                        principalTable: "ProductTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductRecipes_ResourceTypes_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalTable: "ResourceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Brands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ProductTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IndustryCategory = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Awareness = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false),
                    Quality = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Brands_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Buildings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    PowerConsumption = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IsForSale = table.Column<bool>(type: "INTEGER", nullable: false),
                    AskingPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    PricePerSqm = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    OccupancyPercent = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: true),
                    TotalAreaSqm = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    PowerPlantType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    PowerOutput = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    PowerStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    InterestRate = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: true),
                    BuiltAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Buildings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Buildings_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Buildings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyCitySalarySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalaryMultiplier = table.Column<decimal>(type: "TEXT", precision: 8, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyCitySalarySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyCitySalarySettings_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanyCitySalarySettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StartupPackOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OfferKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ShownAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DismissedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClaimedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompanyCashGrant = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ProDurationDays = table.Column<int>(type: "INTEGER", nullable: false),
                    GrantedCompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StartupPackOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StartupPackOffers_Companies_GrantedCompanyId",
                        column: x => x.GrantedCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StartupPackOffers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuildingConfigurationPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubmittedAtTick = table.Column<long>(type: "INTEGER", nullable: false),
                    AppliesAtTick = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalTicksRequired = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingConfigurationPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingConfigurationPlans_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuildingLots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    District = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    PopulationIndex = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: false),
                    BasePrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SuitableTypes = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OwnerCompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BuildingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResourceTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MaterialQuality = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: true),
                    MaterialQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingLots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingLots_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BuildingLots_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildingLots_Companies_OwnerCompanyId",
                        column: x => x.OwnerCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BuildingLots_ResourceTypes_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalTable: "ResourceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BuildingUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UnitType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    GridX = table.Column<int>(type: "INTEGER", nullable: false),
                    GridY = table.Column<int>(type: "INTEGER", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    LinkUp = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkDown = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkLeft = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkRight = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkUpLeft = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkUpRight = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkDownLeft = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkDownRight = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResourceTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProductTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MinPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    MaxPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    PurchaseSource = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SaleVisibility = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Budget = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    MediaHouseBuildingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MinQuality = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: true),
                    BrandScope = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    VendorLockCompanyId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingUnits_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExchangeBuildingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Side = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ResourceTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProductTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PricePerUnit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RemainingQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    MinQuality = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExchangeOrders_Buildings_ExchangeBuildingId",
                        column: x => x.ExchangeBuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExchangeOrders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuildingConfigurationPlanRemovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildingConfigurationPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GridX = table.Column<int>(type: "INTEGER", nullable: false),
                    GridY = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtTick = table.Column<long>(type: "INTEGER", nullable: false),
                    AppliesAtTick = table.Column<long>(type: "INTEGER", nullable: false),
                    TicksRequired = table.Column<int>(type: "INTEGER", nullable: false),
                    IsReverting = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingConfigurationPlanRemovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingConfigurationPlanRemovals_BuildingConfigurationPlans_BuildingConfigurationPlanId",
                        column: x => x.BuildingConfigurationPlanId,
                        principalTable: "BuildingConfigurationPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuildingConfigurationPlanUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildingConfigurationPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UnitType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    GridX = table.Column<int>(type: "INTEGER", nullable: false),
                    GridY = table.Column<int>(type: "INTEGER", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    LinkUp = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkDown = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkLeft = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkRight = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkUpLeft = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkUpRight = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkDownLeft = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkDownRight = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartedAtTick = table.Column<long>(type: "INTEGER", nullable: false),
                    AppliesAtTick = table.Column<long>(type: "INTEGER", nullable: false),
                    TicksRequired = table.Column<int>(type: "INTEGER", nullable: false),
                    IsChanged = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsReverting = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResourceTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProductTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MinPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    MaxPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    PurchaseSource = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SaleVisibility = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Budget = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    MediaHouseBuildingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MinQuality = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: true),
                    BrandScope = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    VendorLockCompanyId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingConfigurationPlanUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingConfigurationPlanUnits_BuildingConfigurationPlans_BuildingConfigurationPlanId",
                        column: x => x.BuildingConfigurationPlanId,
                        principalTable: "BuildingConfigurationPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuildingUnitResourceHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildingUnitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProductTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Tick = table.Column<long>(type: "INTEGER", nullable: false),
                    InflowQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    OutflowQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ProducedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildingUnitResourceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildingUnitResourceHistories_BuildingUnits_BuildingUnitId",
                        column: x => x.BuildingUnitId,
                        principalTable: "BuildingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildingUnitResourceHistories_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildingUnitResourceHistories_ProductTypes_ProductTypeId",
                        column: x => x.ProductTypeId,
                        principalTable: "ProductTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BuildingUnitResourceHistories_ResourceTypes_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalTable: "ResourceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Inventories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildingUnitId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResourceTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProductTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    SourcingCostTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Quality = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false),
                    BrandId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inventories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Inventories_BuildingUnits_BuildingUnitId",
                        column: x => x.BuildingUnitId,
                        principalTable: "BuildingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Inventories_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Inventories_ProductTypes_ProductTypeId",
                        column: x => x.ProductTypeId,
                        principalTable: "ProductTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Inventories_ResourceTypes_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalTable: "ResourceTypes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BuildingUnitId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RecordedAtTick = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProductTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResourceTypeId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_BuildingUnits_BuildingUnitId",
                        column: x => x.BuildingUnitId,
                        principalTable: "BuildingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_ProductTypes_ProductTypeId",
                        column: x => x.ProductTypeId,
                        principalTable: "ProductTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_ResourceTypes_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalTable: "ResourceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PublicSalesRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildingUnitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResourceTypeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Tick = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QuantitySold = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PricePerUnit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Revenue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Demand = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    SalesCapacity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicSalesRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublicSalesRecords_BuildingUnits_BuildingUnitId",
                        column: x => x.BuildingUnitId,
                        principalTable: "BuildingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PublicSalesRecords_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublicSalesRecords_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublicSalesRecords_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublicSalesRecords_ProductTypes_ProductTypeId",
                        column: x => x.ProductTypeId,
                        principalTable: "ProductTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PublicSalesRecords_ResourceTypes_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalTable: "ResourceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Brands_CompanyId",
                table: "Brands",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingConfigurationPlanRemovals_BuildingConfigurationPlanId",
                table: "BuildingConfigurationPlanRemovals",
                column: "BuildingConfigurationPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingConfigurationPlans_BuildingId",
                table: "BuildingConfigurationPlans",
                column: "BuildingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildingConfigurationPlanUnits_BuildingConfigurationPlanId",
                table: "BuildingConfigurationPlanUnits",
                column: "BuildingConfigurationPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingLots_BuildingId",
                table: "BuildingLots",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingLots_CityId",
                table: "BuildingLots",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingLots_OwnerCompanyId",
                table: "BuildingLots",
                column: "OwnerCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingLots_ResourceTypeId",
                table: "BuildingLots",
                column: "ResourceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_CityId",
                table: "Buildings",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_CompanyId",
                table: "Buildings",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingUnitResourceHistories_BuildingId_Tick",
                table: "BuildingUnitResourceHistories",
                columns: new[] { "BuildingId", "Tick" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildingUnitResourceHistories_BuildingUnitId_Tick_ResourceTypeId_ProductTypeId",
                table: "BuildingUnitResourceHistories",
                columns: new[] { "BuildingUnitId", "Tick", "ResourceTypeId", "ProductTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildingUnitResourceHistories_ProductTypeId",
                table: "BuildingUnitResourceHistories",
                column: "ProductTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingUnitResourceHistories_ResourceTypeId",
                table: "BuildingUnitResourceHistories",
                column: "ResourceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildingUnits_BuildingId",
                table: "BuildingUnits",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_CityResources_CityId",
                table: "CityResources",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_CityResources_ResourceTypeId",
                table: "CityResources",
                column: "ResourceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_PlayerId",
                table: "Companies",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyCitySalarySettings_CityId",
                table: "CompanyCitySalarySettings",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyCitySalarySettings_CompanyId_CityId",
                table: "CompanyCitySalarySettings",
                columns: new[] { "CompanyId", "CityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeOrders_CompanyId",
                table: "ExchangeOrders",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeOrders_ExchangeBuildingId",
                table: "ExchangeOrders",
                column: "ExchangeBuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_Inventories_BuildingId",
                table: "Inventories",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_Inventories_BuildingUnitId",
                table: "Inventories",
                column: "BuildingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Inventories_ProductTypeId",
                table: "Inventories",
                column: "ProductTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Inventories_ResourceTypeId",
                table: "Inventories",
                column: "ResourceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_BuildingId",
                table: "LedgerEntries",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_BuildingUnitId",
                table: "LedgerEntries",
                column: "BuildingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CompanyId_RecordedAtTick",
                table: "LedgerEntries",
                columns: new[] { "CompanyId", "RecordedAtTick" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_ProductTypeId",
                table: "LedgerEntries",
                column: "ProductTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_ResourceTypeId",
                table: "LedgerEntries",
                column: "ResourceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_Email",
                table: "Players",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductRecipes_InputProductTypeId",
                table: "ProductRecipes",
                column: "InputProductTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductRecipes_ProductTypeId",
                table: "ProductRecipes",
                column: "ProductTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductRecipes_ResourceTypeId",
                table: "ProductRecipes",
                column: "ResourceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductTypes_Slug",
                table: "ProductTypes",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicSalesRecords_BuildingId",
                table: "PublicSalesRecords",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicSalesRecords_BuildingUnitId_Tick",
                table: "PublicSalesRecords",
                columns: new[] { "BuildingUnitId", "Tick" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicSalesRecords_CityId",
                table: "PublicSalesRecords",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicSalesRecords_CompanyId_Tick",
                table: "PublicSalesRecords",
                columns: new[] { "CompanyId", "Tick" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicSalesRecords_ProductTypeId",
                table: "PublicSalesRecords",
                column: "ProductTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicSalesRecords_ResourceTypeId",
                table: "PublicSalesRecords",
                column: "ResourceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceTypes_Slug",
                table: "ResourceTypes",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StartupPackOffers_GrantedCompanyId",
                table: "StartupPackOffers",
                column: "GrantedCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_StartupPackOffers_PlayerId",
                table: "StartupPackOffers",
                column: "PlayerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Brands");

            migrationBuilder.DropTable(
                name: "BuildingConfigurationPlanRemovals");

            migrationBuilder.DropTable(
                name: "BuildingConfigurationPlanUnits");

            migrationBuilder.DropTable(
                name: "BuildingLots");

            migrationBuilder.DropTable(
                name: "BuildingUnitResourceHistories");

            migrationBuilder.DropTable(
                name: "CityResources");

            migrationBuilder.DropTable(
                name: "CompanyCitySalarySettings");

            migrationBuilder.DropTable(
                name: "ExchangeOrders");

            migrationBuilder.DropTable(
                name: "GameStates");

            migrationBuilder.DropTable(
                name: "Inventories");

            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "ProductRecipes");

            migrationBuilder.DropTable(
                name: "PublicSalesRecords");

            migrationBuilder.DropTable(
                name: "StartupPackOffers");

            migrationBuilder.DropTable(
                name: "BuildingConfigurationPlans");

            migrationBuilder.DropTable(
                name: "BuildingUnits");

            migrationBuilder.DropTable(
                name: "ProductTypes");

            migrationBuilder.DropTable(
                name: "ResourceTypes");

            migrationBuilder.DropTable(
                name: "Buildings");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "Players");
        }
    }
}
