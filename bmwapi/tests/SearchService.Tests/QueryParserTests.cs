using SearchService.Endpoints;
using SearchService.Models;
using SearchService.Services;

namespace SearchService.Tests;

public class QueryParserTests
{
    private static ParsedQuery Parse(string text) => QueryParser.Parse(text, TestDictionary.Dictionary);

    [Fact]
    public void YearModelPositionAndPartType()
    {
        var q = Parse("2016 hilux front bumper");

        Assert.Equal(2016, q.YearFrom);
        Assert.Equal("Toyota", q.Make); // from the model
        Assert.Equal("Hilux", q.Model);
        Assert.Equal("Bumper", q.PartType!.Name);
        Assert.Equal("Front", q.Position);
        Assert.Equal("", q.Keywords);
        Assert.Equal("toyota|hilux|2016", q.VehicleKey());
        Assert.Equal(["partType", "make", "model", "year"], q.Chips().Select(c => c.Type));
        Assert.Equal("Front bumper", q.PartLabel());
    }

    [Theory]
    [InlineData("bonnet swift", "Bonnet", "Maruti Suzuki", "Swift")]
    [InlineData("hood for swift", "Bonnet", "Maruti Suzuki", "Swift")]
    [InlineData("dickey wagonr", "Boot lid", "Maruti Suzuki", "Wagon R")]
    [InlineData("maruti wagon r trunk", "Boot lid", "Maruti Suzuki", "Wagon R")]
    [InlineData("beemer 330i headlight", "Headlamp", "BMW", "3 Series")]
    [InlineData("chevy silverado tail light", "Tail lamp", "Chevrolet", "Silverado")]
    [InlineData("f-150 headlamp", "Headlamp", "Ford", "F-150")]
    [InlineData("F150 HEADLIGHT", "Headlamp", "Ford", "F-150")]
    [InlineData("merc c300 muffler", "Silencer", "Mercedes-Benz", "C-Class")]
    public void SynonymsAliasesAndLocalWords(string text, string partType, string make, string model)
    {
        var q = Parse(text);

        Assert.Equal(partType, q.PartType?.Name);
        Assert.Equal(make, q.Make);
        Assert.Equal(model, q.Model);
    }

    [Fact]
    public void YearRange()
    {
        var q = Parse("toyota hilux 2015-2018 bumper");

        Assert.Equal(2015, q.YearFrom);
        Assert.Equal(2018, q.YearTo);
    }

    [Theory]
    [InlineData("52119-0K935", "521190K935")]
    [InlineData("34116860912", "34116860912")]
    [InlineData("bumper 52119-0k935", "521190K935")]
    public void PartNumbers(string text, string expected) => Assert.Equal(expected, Parse(text).PartNumber);

    [Theory]
    [InlineData("265/65 r17 tyre")]
    [InlineData("265 65 17")]
    [InlineData("265/65R17")]
    public void TyreSizes(string text)
    {
        var q = Parse(text);

        Assert.Equal("Tyre", q.PartType?.Name);
        Assert.Equal("265/65r17", q.TyreSize);
        Assert.Null(q.PartNumber);
        Assert.Null(q.YearFrom);
    }

    [Fact]
    public void LeftRightAndRhLh()
    {
        Assert.Equal("Left", Parse("swift headlamp left").Side);
        Assert.Equal("Right", Parse("rh headlight").Side);
        Assert.Equal("Rear", Parse("back bumper").Position);
    }

    [Fact]
    public void UnknownWords_AreKeywords()
    {
        var q = Parse("hilux bumper with fog lamp holes");

        Assert.Equal("fog lamp holes", q.Keywords);
        Assert.Equal("Bumper", q.PartType?.Name);
    }

    [Fact]
    public void NumericModelNames_NeedTheirMake()
    {
        Assert.Null(Parse("406").Model);
        Assert.Equal("406", Parse("peugeot 406").Model);
    }

    [Fact]
    public void Filter_VehiclePlusPartType_OrCommonPartsOfThatType()
    {
        var filter = ListingSearchEndpoints.UnderstoodFilter(Parse("2016 hilux front bumper"));

        Assert.Equal("((fitKeys = \"toyota|hilux|2016\" AND partType = \"Bumper\") OR (fitmentMode IN [\"Universal\", \"SizeBased\"] AND partType = \"Bumper\"))", filter);
        Assert.Equal("partNumbersNormalized = \"521190K935\"", ListingSearchEndpoints.UnderstoodFilter(Parse("52119-0K935")));
        Assert.Equal("(partType = \"Tyre\" AND specKeys = \"tyresize:265/65r17\")", ListingSearchEndpoints.UnderstoodFilter(Parse("265 65 17")));
    }

    [Fact]
    public void FitKeys_OneKeyPerYear()
    {
        var item = new Item
        {
            Id = "x", Seller = "s", Status = "Live", Make = "", Model = "", Description = "",
            FitmentMode = "VehicleSpecific",
            Fitments = [new Contracts.FitmentInfo { YearFrom = 2016, YearTo = 2018, Make = "Toyota", Model = "Hilux" }],
            PartNumbers = ["52119-0K935"],
            Specs = new() { ["width"] = "265", ["profile"] = "65", ["rim"] = "17" }
        }.Prepare();

        Assert.Equal(["toyota", "toyota|hilux", "toyota|hilux|2016", "toyota|hilux|2017", "toyota|hilux|2018"], item.FitKeys);
        Assert.Equal(["521190K935"], item.PartNumbersNormalized);
        Assert.Contains("tyresize:265/65r17", item.SpecKeys);
    }
}
