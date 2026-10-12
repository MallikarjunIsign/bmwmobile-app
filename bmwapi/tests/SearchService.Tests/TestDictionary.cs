using SearchService.Services;

namespace SearchService.Tests;

/// <summary>A small slice of the real master data (AuctionService CatalogSeed) for search tests.</summary>
internal static class TestDictionary
{
    public static DictionaryData Data => new()
    {
        Makes =
        [
            new DictionaryMake
            {
                Name = "Toyota", Countries = ["IN", "NG", "US"],
                Models = [new DictionaryModel { Name = "Hilux" }, new DictionaryModel { Name = "Corolla" }, new DictionaryModel { Name = "Fortuner" }]
            },
            new DictionaryMake
            {
                Name = "Maruti Suzuki", Aliases = ["maruti"], Countries = ["IN"],
                Models = [new DictionaryModel { Name = "Swift" }, new DictionaryModel { Name = "Wagon R", Aliases = ["wagonr"] }]
            },
            new DictionaryMake
            {
                Name = "BMW", Aliases = ["beemer", "bimmer"],
                Models = [new DictionaryModel { Name = "3 Series", Aliases = ["330i", "320d"] }]
            },
            new DictionaryMake { Name = "Chevrolet", Aliases = ["chevy"], Models = [new DictionaryModel { Name = "Silverado" }] },
            new DictionaryMake { Name = "Ford", Models = [new DictionaryModel { Name = "F-150", Aliases = ["f150"] }, new DictionaryModel { Name = "Ranger" }] },
            new DictionaryMake { Name = "Peugeot", Models = [new DictionaryModel { Name = "406" }] },
            new DictionaryMake { Name = "Mercedes-Benz", Aliases = ["merc", "benz"], Models = [new DictionaryModel { Name = "C-Class", Aliases = ["c300"] }] },
        ],
        PartTypes =
        [
            new DictionaryPartType { Id = 1, Name = "Bumper", Category = "Body – Exterior", FitmentMode = "VehicleSpecific", Synonyms = ["bumper cover", "front bumper", "rear bumper"] },
            new DictionaryPartType { Id = 2, Name = "Bonnet", Category = "Body – Exterior", FitmentMode = "VehicleSpecific", Synonyms = ["hood", "engine hood"] },
            new DictionaryPartType { Id = 3, Name = "Headlamp", Category = "Lighting", FitmentMode = "VehicleSpecific", Synonyms = ["headlight", "head light"] },
            new DictionaryPartType { Id = 4, Name = "Tyre", Category = "Wheels & Tyres", FitmentMode = "SizeBased", Synonyms = ["tire", "tyres", "tires"] },
            new DictionaryPartType { Id = 5, Name = "Dash cam", Category = "Accessories & Tools", FitmentMode = "Universal", Synonyms = ["dashcam"] },
            new DictionaryPartType { Id = 6, Name = "Boot lid", Category = "Body – Exterior", FitmentMode = "VehicleSpecific", Synonyms = ["trunk", "dickey", "dicky"] },
            new DictionaryPartType { Id = 7, Name = "Silencer", Category = "Fuel & Exhaust", FitmentMode = "VehicleSpecific", Synonyms = ["muffler", "exhaust"] },
            new DictionaryPartType { Id = 8, Name = "Tail lamp", Category = "Lighting", FitmentMode = "VehicleSpecific", Synonyms = ["rear light", "tail light"] },
        ]
    };

    public static SearchDictionary Dictionary => new(Data);
}
