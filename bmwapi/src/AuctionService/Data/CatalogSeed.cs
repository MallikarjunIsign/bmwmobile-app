using AuctionService.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Data;

/// <summary>
/// Starter master data (design doc §3 and §8.1): the part catalogue with fitment modes and synonyms (incl. local
/// words: bonnet = hood, boot / dickey = trunk, silencer = muffler, tyre = tire, windscreen = windshield,
/// wing = fender, number plate = licence plate, gearbox = transmission, indicator = turn signal) and popular makes /
/// models for India, the USA, Nigeria and Ireland with aliases (Chevy, Merc, Beemer / Bimmer, VW).
/// Added once, when the tables are empty; admins extend it later.
/// </summary>
public static class CatalogSeed
{
    private const FitmentMode V = FitmentMode.VehicleSpecific;
    private const FitmentMode S = FitmentMode.SizeBased;
    private const FitmentMode U = FitmentMode.Universal;

    private record PT(string Name, FitmentMode Mode, string[] Synonyms, bool Position = false, bool Side = false,
        bool Body = false, string[]? Important = null, SpecFieldDefinition[]? Specs = null, string? Sub = null);

    private static SpecFieldDefinition Num(string key, string label, string? unit = null, bool required = true) =>
        new() { Key = key, Label = label, Type = "number", Unit = unit, Required = required };

    private static SpecFieldDefinition Txt(string key, string label, bool required = true) =>
        new() { Key = key, Label = label, Type = "text", Required = required };

    private static SpecFieldDefinition Sel(string key, string label, params string[] options) =>
        new() { Key = key, Label = label, Type = "select", Options = options.ToList() };

    private static readonly (string Name, PartCategory Legacy, PT[] Types)[] Catalogue =
    [
        ("Engine & Mechanical", PartCategory.Engine,
        [
            new("Engine assembly", V, ["engine", "motor", "complete engine", "long block"], Important: ["Engine", "Fuel"]),
            new("Cylinder head", V, ["head", "engine head"], Important: ["Engine"]),
            new("Turbocharger", V, ["turbo"], Important: ["Engine"]),
            new("Alternator", V, ["dynamo", "generator"], Important: ["Engine"]),
            new("Starter motor", V, ["starter", "self starter", "self motor"], Important: ["Engine"]),
            new("Timing kit", V, ["timing belt", "timing chain", "cam belt"], Important: ["Engine"]),
        ]),
        ("Transmission & Drivetrain", PartCategory.Transmission,
        [
            new("Gearbox", V, ["transmission", "gear box", "tranny"], Important: ["Transmission", "Drivetrain", "Engine"]),
            new("Clutch kit", V, ["clutch", "clutch plate", "pressure plate"], Important: ["Transmission", "Engine"]),
            new("CV axle", V, ["driveshaft", "drive shaft", "half shaft", "cv joint"], Side: true, Important: ["Drivetrain", "Transmission"]),
            new("Differential", V, ["diff", "rear diff", "crown wheel"], Important: ["Drivetrain"]),
            new("Transfer case", V, ["transfer box"], Important: ["Drivetrain"]),
        ]),
        ("Suspension & Steering", PartCategory.Suspension,
        [
            new("Shock absorber", V, ["shocker", "shock", "shocks", "damper"], Position: true, Side: true),
            new("Strut", V, ["mcpherson strut", "strut assembly"], Position: true, Side: true),
            new("Control arm", V, ["wishbone", "lower arm", "track control arm"], Position: true, Side: true),
            new("Steering rack", V, ["steering box", "rack and pinion", "power steering rack"], Important: ["SteeringSide"]),
            new("Wheel hub bearing", V, ["wheel bearing", "hub", "hub bearing"], Position: true, Side: true),
        ]),
        ("Brakes", PartCategory.Brakes,
        [
            new("Brake pads", V, ["brake pad", "disc pads", "pads"], Position: true),
            new("Brake discs", V, ["brake disc", "rotor", "rotors", "brake rotor", "disc"], Position: true),
            new("Brake caliper", V, ["caliper", "calliper"], Position: true, Side: true),
            new("ABS module", V, ["abs pump", "abs unit", "abs"]),
        ]),
        ("Electrical & Electronics", PartCategory.Electrical,
        [
            new("ECU", V, ["engine control unit", "ecm", "computer", "brain box", "engine computer"], Important: ["Engine", "Transmission"]),
            new("Wiring harness", V, ["wiring", "loom", "wire harness"]),
            new("Sensor", V, ["o2 sensor", "oxygen sensor", "map sensor", "abs sensor", "parking sensor"]),
            new("Infotainment unit", V, ["stereo", "head unit", "music system", "radio", "car stereo"]),
            new("Battery", S, ["car battery", "accumulator"], Specs:
            [
                Num("capacityAh", "Capacity", "Ah"),
                Sel("voltage", "Voltage", "12V", "24V"),
                Sel("terminalLayout", "Terminal layout", "Left +", "Right +"),
                Txt("sizeGroup", "Size group", required: false),
            ]),
        ]),
        ("Lighting", PartCategory.Lighting,
        [
            new("Headlamp", V, ["headlight", "head light", "head lamp", "headlamps", "headlights"], Side: true, Important: ["SteeringSide"], Sub: "Lamps"),
            new("Tail lamp", V, ["taillight", "tail light", "rear light", "back light", "tail lights"], Side: true, Sub: "Lamps"),
            new("Fog lamp", V, ["fog light", "foglamp", "fog lights"], Side: true, Sub: "Lamps"),
            new("Indicator", V, ["turn signal", "blinker", "indicator light", "side indicator", "turn signal light"], Position: true, Side: true, Sub: "Lamps"),
            new("Bulb", S, ["bulbs", "light bulb", "h4", "h7", "h11", "headlight bulb"], Specs:
            [
                Sel("fitting", "Fitting", "H1", "H3", "H4", "H7", "H11", "HB3/9005", "HB4/9006", "T10", "P21W", "PY21W"),
                Num("wattage", "Wattage", "W", required: false),
            ], Sub: "Bulbs"),
            new("LED kit", S, ["led conversion", "led headlight kit", "led bulbs"], Specs:
            [
                Sel("fitting", "Fitting", "H1", "H3", "H4", "H7", "H11", "HB3/9005", "HB4/9006"),
            ], Sub: "Bulbs"),
        ]),
        ("Body – Exterior", PartCategory.Body,
        [
            new("Bumper", V, ["bumper cover", "front bumper", "rear bumper", "fascia"], Position: true, Body: true, Sub: "Bumpers & grilles"),
            new("Grille", V, ["grill", "radiator grille", "front grille"], Body: true, Sub: "Bumpers & grilles"),
            new("Bonnet", V, ["hood", "engine hood", "bonnet panel"], Body: true, Sub: "Panels & doors"),
            new("Fender", V, ["wing", "mudguard", "fender panel", "front wing"], Side: true, Body: true, Sub: "Panels & doors"),
            new("Door", V, ["door shell", "door panel", "car door"], Position: true, Side: true, Body: true, Sub: "Panels & doors"),
            new("Boot lid", V, ["trunk", "trunk lid", "dickey", "dicky", "tailgate", "hatch", "boot", "dickey door"], Body: true, Sub: "Panels & doors"),
            new("Side mirror", V, ["wing mirror", "door mirror", "side view mirror", "orvm", "outside mirror", "mirror"], Side: true, Body: true, Important: ["SteeringSide"], Sub: "Mirrors"),
            new("Licence plate frame", U, ["number plate", "license plate", "number plate frame", "licence plate", "plate holder"]),
        ]),
        ("Glass", PartCategory.Body,
        [
            new("Windscreen", V, ["windshield", "front glass", "front windscreen", "front windshield"]),
            new("Door glass", V, ["window glass", "side glass", "door window"], Position: true, Side: true),
            new("Rear glass", V, ["rear windscreen", "back glass", "rear windshield"]),
        ]),
        ("Interior", PartCategory.Interior,
        [
            new("Seat", V, ["seats", "car seat"], Position: true, Side: true),
            new("Dashboard", V, ["dash", "dash board", "instrument panel"], Important: ["SteeringSide"]),
            new("Steering wheel", V, ["steering", "sterring wheel"], Important: ["SteeringSide"]),
            new("Door trim", V, ["door card", "door pad", "door trim panel"], Position: true, Side: true),
            new("Seat covers", U, ["seat cover", "seat covers universal"]),
            new("Floor mats", U, ["mats", "car mats", "floor mat", "foot mats"]),
        ]),
        ("Wheels & Tyres", PartCategory.WheelsAndTyres,
        [
            new("Alloy wheels", V, ["alloys", "rims", "alloy rims", "mag wheels", "alloy wheel"]),
            new("Tyre", S, ["tire", "tyres", "tires", "car tyre"], Specs:
            [
                Num("width", "Width", "mm"),
                Num("profile", "Profile"),
                Num("rim", "Rim size", "in"),
                Txt("loadIndex", "Load index", required: false),
                Txt("speedRating", "Speed rating", required: false),
            ]),
            new("Wheel cover", U, ["hubcap", "wheel cap", "hub cap"]),
        ]),
        ("Cooling & AC", PartCategory.Engine,
        [
            new("Radiator", V, ["rad", "engine radiator"], Important: ["Engine"]),
            new("AC compressor", V, ["ac pump", "air con compressor", "compressor", "a/c compressor"], Important: ["Engine"]),
            new("Condenser", V, ["ac condenser", "a/c condenser"]),
            new("Radiator fan", V, ["cooling fan", "fan", "radiator fan motor"]),
        ]),
        ("Fuel & Exhaust", PartCategory.Engine,
        [
            new("Fuel pump", V, ["petrol pump", "diesel pump"], Important: ["Engine", "Fuel"]),
            new("Fuel injector", V, ["injector", "injectors"], Important: ["Engine", "Fuel"]),
            new("Silencer", V, ["muffler", "exhaust", "exhaust pipe", "back box"]),
            new("Catalytic converter", V, ["cat", "catalyst", "cat converter"], Important: ["Engine"]),
        ]),
        ("Filters & Fluids", PartCategory.Engine,
        [
            new("Oil filter", V, ["engine oil filter"], Important: ["Engine"]),
            new("Air filter", V, ["air cleaner", "engine air filter"], Important: ["Engine"]),
            new("Fuel filter", V, ["diesel filter", "petrol filter"], Important: ["Engine", "Fuel"]),
            new("Engine oil", S, ["motor oil", "oil", "lubricant", "lube"], Specs:
            [
                Sel("grade", "Grade", "0W-20", "5W-30", "5W-40", "10W-30", "10W-40", "15W-40", "20W-50"),
                Num("volume", "Volume", "L"),
            ]),
            new("Coolant", S, ["antifreeze", "radiator coolant"], Specs:
            [
                Txt("type", "Type"),
                Num("volume", "Volume", "L"),
            ]),
            new("Wiper blade", S, ["wiper", "wipers", "wiper blades"], Specs:
            [
                Num("length", "Length", "in"),
            ]),
        ]),
        ("Accessories & Tools", PartCategory.Other,
        [
            new("Dash cam", U, ["dashcam", "dashboard camera", "car camera", "dvr"]),
            new("Phone holder", U, ["phone mount", "mobile holder", "mobile stand"]),
            new("Car cover", U, ["body cover", "car body cover"]),
            new("Tool kit", U, ["tools", "toolkit", "tool set"]),
            new("Jump starter", U, ["booster", "jump pack", "jumper cables", "battery booster"]),
            new("Car care", U, ["polish", "wax", "car shampoo", "cleaner"]),
        ]),
    ];

    private record M(string Name, string[] Countries, string[]? Aliases = null, string? Body = null);

    private static readonly (string Make, string[] Countries, string[] Aliases, M[] Models)[] Vehicles =
    [
        ("Maruti Suzuki", ["IN"], ["maruti", "maruthi"],
        [
            new("Swift", ["IN"]), new("Baleno", ["IN"]), new("Dzire", ["IN"], ["swift dzire"]), new("Alto", ["IN"], ["alto 800", "alto k10"]),
            new("Wagon R", ["IN"], ["wagonr", "wagon-r"]), new("Ertiga", ["IN"]), new("Brezza", ["IN"], ["vitara brezza"]), new("Celerio", ["IN"]),
        ]),
        ("Hyundai", ["IN", "US", "NG", "IE"], [],
        [
            new("Creta", ["IN"]), new("i20", ["IN", "IE"]), new("Venue", ["IN", "US"]), new("Verna", ["IN"]), new("Grand i10", ["IN"], ["i10"]),
            new("Elantra", ["US", "NG"]), new("Tucson", ["US", "NG", "IE"]), new("Santa Fe", ["US", "NG"]), new("Sonata", ["US", "NG"]),
            new("Kona", ["IE", "US"]),
        ]),
        ("Tata", ["IN"], ["tata motors"],
        [
            new("Nexon", ["IN"]), new("Punch", ["IN"]), new("Tiago", ["IN"]), new("Harrier", ["IN"]), new("Safari", ["IN"]), new("Altroz", ["IN"]),
        ]),
        ("Mahindra", ["IN"], ["mahindra & mahindra"],
        [
            new("Scorpio", ["IN"], ["scorpio n"]), new("XUV700", ["IN"], ["xuv 700"]), new("Thar", ["IN"]), new("Bolero", ["IN"]),
            new("XUV300", ["IN"], ["xuv 300", "xuv 3xo"]),
        ]),
        ("Toyota", ["IN", "US", "NG", "IE"], [],
        [
            new("Hilux", ["IN", "NG", "IE"], Body: "Pickup"), new("Corolla", ["US", "NG", "IE"]), new("Camry", ["US", "NG"]),
            new("Fortuner", ["IN", "NG"]), new("Innova", ["IN"], ["innova crysta", "innova hycross"]), new("RAV4", ["US", "IE", "NG"], ["rav 4"]),
            new("Land Cruiser", ["NG", "US"], ["landcruiser", "prado", "land cruiser prado"]), new("Highlander", ["US", "NG"]),
            new("Sienna", ["US", "NG"]), new("Yaris", ["IE", "NG"]), new("Tacoma", ["US"], Body: "Pickup"), new("Venza", ["US", "NG"]),
            new("Hiace", ["NG"], ["hiace bus"]),
        ]),
        ("Honda", ["IN", "US", "NG", "IE"], [],
        [
            new("City", ["IN"]), new("Amaze", ["IN"]), new("Civic", ["US", "IN", "IE", "NG"]), new("Accord", ["US", "NG"]),
            new("CR-V", ["US", "NG", "IE"], ["crv", "cr v"]), new("Pilot", ["US", "NG"]), new("Jazz", ["IN", "IE"], ["fit", "honda fit"]),
            new("Activa", ["IN"], Body: "Scooter"), new("Shine", ["IN"], ["cb shine"], Body: "Motorcycle"),
        ]),
        ("Ford", ["US", "NG", "IE", "IN"], [],
        [
            new("F-150", ["US"], ["f150", "f 150"], "Pickup"), new("Ranger", ["US", "NG", "IE"], Body: "Pickup"), new("Mustang", ["US"]),
            new("Explorer", ["US", "NG"]), new("Escape", ["US", "NG"]), new("Focus", ["IE", "US", "NG"]), new("Fiesta", ["IE"]),
            new("EcoSport", ["IN", "NG"], ["eco sport"]), new("Transit", ["IE", "US"], Body: "Van"), new("Edge", ["US", "NG"]),
        ]),
        ("Chevrolet", ["US", "NG"], ["chevy", "chev"],
        [
            new("Silverado", ["US"], Body: "Pickup"), new("Malibu", ["US", "NG"]), new("Equinox", ["US"]), new("Tahoe", ["US"]),
            new("Camaro", ["US"]), new("Cruze", ["NG", "US"]),
        ]),
        ("Nissan", ["IN", "US", "NG", "IE"], [],
        [
            new("Altima", ["US", "NG"]), new("Sentra", ["US", "NG"]), new("Rogue", ["US"]), new("Navara", ["NG", "IE"], ["frontier"], "Pickup"),
            new("Qashqai", ["IE"]), new("X-Trail", ["IE", "NG"], ["xtrail", "x trail"]), new("Pathfinder", ["US", "NG"]),
            new("Almera", ["NG"]), new("Micra", ["IE", "IN"]), new("Magnite", ["IN"]),
        ]),
        ("Kia", ["IN", "US", "NG", "IE"], [],
        [
            new("Seltos", ["IN"]), new("Sonet", ["IN"]), new("Carens", ["IN"]), new("Sportage", ["US", "IE", "NG"]),
            new("Sorento", ["US", "NG"]), new("Rio", ["NG", "US"]), new("Picanto", ["NG", "IE"]), new("Optima", ["NG", "US"], ["k5"]),
        ]),
        ("Volkswagen", ["IN", "US", "NG", "IE"], ["vw", "volks", "volkswagon"],
        [
            new("Golf", ["IE", "US", "NG"]), new("Polo", ["IN", "IE"]), new("Passat", ["IE", "US", "NG"]), new("Jetta", ["US", "NG"]),
            new("Tiguan", ["IE", "US", "IN"]), new("Virtus", ["IN"]), new("Taigun", ["IN"]),
        ]),
        ("BMW", ["IN", "US", "NG", "IE"], ["beemer", "bimmer", "beamer", "bmw"],
        [
            new("3 Series", ["IN", "US", "NG", "IE"], ["3er", "320i", "320d", "330i", "328i", "3-series"]),
            new("5 Series", ["IN", "US", "NG", "IE"], ["5er", "520d", "530i", "5-series"]),
            new("X1", ["IN", "IE"]), new("X3", ["US", "IE", "NG"]), new("X5", ["US", "NG", "IN"]),
        ]),
        ("Mercedes-Benz", ["IN", "US", "NG", "IE"], ["merc", "mercedes", "benz", "mercedes benz"],
        [
            new("C-Class", ["IN", "US", "NG", "IE"], ["c class", "c200", "c300", "c 300", "c-class"]),
            new("E-Class", ["IN", "US", "NG", "IE"], ["e class", "e300", "e350"]),
            new("GLC", ["US", "IE", "NG"], ["glc 300"]), new("GLE", ["US", "NG"], ["ml", "ml350", "gle 350"]),
            new("Sprinter", ["IE", "US", "NG"], Body: "Van"),
        ]),
        ("Lexus", ["US", "NG"], [],
        [
            new("RX", ["US", "NG"], ["rx350", "rx 350", "rx330"]), new("ES", ["US", "NG"], ["es350", "es 350"]),
            new("GX", ["US", "NG"], ["gx460", "gx 460"]), new("LX", ["NG", "US"], ["lx570", "lx 570"]),
        ]),
        ("Skoda", ["IN", "IE"], ["škoda"],
        [
            new("Octavia", ["IN", "IE"]), new("Superb", ["IN", "IE"]), new("Kushaq", ["IN"]), new("Slavia", ["IN"]),
            new("Kodiaq", ["IE", "IN"]), new("Fabia", ["IE"]),
        ]),
        ("Renault", ["IN", "IE"], [],
        [
            new("Kwid", ["IN"]), new("Duster", ["IN", "IE"]), new("Triber", ["IN"]), new("Kiger", ["IN"]), new("Clio", ["IE"]), new("Megane", ["IE"]),
        ]),
        ("Peugeot", ["NG", "IE"], [],
        [
            new("406", ["NG"]), new("407", ["NG"]), new("301", ["NG"]), new("3008", ["NG", "IE"]), new("208", ["IE"]), new("508", ["NG", "IE"]),
        ]),
        ("Mitsubishi", ["NG", "IE"], [],
        [
            new("Pajero", ["NG"], ["montero"]), new("L200", ["NG", "IE"], ["triton"], "Pickup"), new("Outlander", ["NG", "IE"]), new("Lancer", ["NG"]),
        ]),
        ("Audi", ["IN", "US", "IE"], [],
        [
            new("A4", ["IN", "US", "IE"]), new("A6", ["IN", "US", "IE"]), new("Q5", ["IN", "US", "IE"]), new("Q7", ["IN", "US"]),
        ]),
        ("Jeep", ["US", "IN"], [],
        [
            new("Wrangler", ["US"]), new("Grand Cherokee", ["US"], ["cherokee"]), new("Compass", ["IN", "US"]),
        ]),
        ("Ram", ["US"], ["dodge ram"],
        [
            new("1500", ["US"], ["ram 1500"], "Pickup"),
        ]),
        ("Tesla", ["US", "IE"], [],
        [
            new("Model 3", ["US", "IE"], ["model3"]), new("Model Y", ["US", "IE"], ["modely"]),
        ]),
        ("Opel", ["IE"], ["vauxhall"],
        [
            new("Astra", ["IE"]), new("Corsa", ["IE"]),
        ]),
        ("Hero", ["IN"], ["hero motocorp"],
        [
            new("Splendor", ["IN"], ["splendor plus"], "Motorcycle"), new("HF Deluxe", ["IN"], ["hf"], "Motorcycle"),
        ]),
        ("Bajaj", ["IN", "NG"], [],
        [
            new("Pulsar", ["IN"], Body: "Motorcycle"), new("Boxer", ["NG"], Body: "Motorcycle"), new("RE", ["NG"], ["keke", "tricycle"], "Tricycle"),
        ]),
        ("Royal Enfield", ["IN"], ["enfield", "re"],
        [
            new("Classic 350", ["IN"], ["classic"], "Motorcycle"), new("Bullet 350", ["IN"], ["bullet"], "Motorcycle"),
        ]),
    ];

    /// <summary>A few known trims / engines (optional fitment fields).</summary>
    private static readonly (string Make, string Model, int From, int To, string? Trim, string? Engine, string? Fuel, string? Transmission, string? Drivetrain, string? Market)[] Configs =
    [
        ("Toyota", "Hilux", 2016, 2020, "SR", "2.4 D-4D", "Diesel", "Manual", "4WD", null),
        ("Toyota", "Hilux", 2016, 2020, "SR5", "2.8 D-4D", "Diesel", "Automatic", "4WD", null),
        ("Toyota", "Hilux", 2016, 2020, "Workmate", "2.7 VVT-i", "Petrol", "Manual", "RWD", null),
        ("Maruti Suzuki", "Swift", 2018, 2023, "VXi", "1.2 K12", "Petrol", "Manual", "FWD", "Indian-market"),
        ("Maruti Suzuki", "Swift", 2018, 2023, "ZXi", "1.2 K12", "Petrol", "AMT", "FWD", "Indian-market"),
        ("Ford", "F-150", 2015, 2020, "XLT", "5.0 V8", "Petrol", "Automatic", "4WD", "US-market"),
        ("Ford", "F-150", 2015, 2020, "Lariat", "3.5 EcoBoost", "Petrol", "Automatic", "4WD", "US-market"),
        ("Toyota", "Corolla", 2014, 2019, "LE", "1.8", "Petrol", "CVT", "FWD", "US-market"),
        ("Honda", "City", 2014, 2019, "V", "1.5 i-VTEC", "Petrol", "Manual", "FWD", "Indian-market"),
        ("Volkswagen", "Golf", 2013, 2019, "Comfortline", "2.0 TDI", "Diesel", "Manual", "FWD", null),
    ];

    /// <summary>Adds the starter catalogue and vehicles when the tables are empty (also in tests).</summary>
    public static void Seed(AuctionDbContext context)
    {
        if (!context.Categories.Any())
        {
            var order = 0;
            foreach (var (name, legacy, types) in Catalogue)
            {
                var top = new Category { Name = name, LegacyCategory = legacy, SortOrder = order++ };
                context.Categories.Add(top);
                var subs = new Dictionary<string, Category>();
                foreach (var t in types)
                {
                    var category = top;
                    if (t.Sub is not null && !subs.TryGetValue(t.Sub, out category))
                    {
                        category = new Category { Name = t.Sub, Parent = top, LegacyCategory = legacy, SortOrder = subs.Count };
                        subs[t.Sub] = category;
                        context.Categories.Add(category);
                    }

                    context.PartTypes.Add(new PartType
                    {
                        Name = t.Name,
                        Category = category,
                        FitmentMode = t.Mode,
                        HasPosition = t.Position,
                        HasSide = t.Side,
                        IsBodyPart = t.Body,
                        Synonyms = t.Synonyms.Select(s => s.ToLowerInvariant()).Distinct().ToArray(),
                        ImportantFitmentFields = t.Important ?? [],
                        SpecFields = (t.Specs ?? []).ToList()
                    });
                }
            }
            context.SaveChanges();
        }

        if (!context.Makes.Any())
        {
            foreach (var (makeName, countries, aliases, models) in Vehicles)
            {
                context.Makes.Add(new Make
                {
                    Name = makeName,
                    Countries = countries,
                    Aliases = aliases.Select(a => a.ToLowerInvariant()).Where(a => a != makeName.ToLowerInvariant()).Distinct().ToArray(),
                    Models = models.Select(m => new VehicleModel
                    {
                        Name = m.Name,
                        Countries = m.Countries,
                        Aliases = (m.Aliases ?? []).Select(a => a.ToLowerInvariant()).Distinct().ToArray(),
                        BodyType = m.Body
                    }).ToList()
                });
            }
            context.SaveChanges();

            var models2 = context.Models.Include(m => m.Make).ToList();
            foreach (var c in Configs)
            {
                var model = models2.FirstOrDefault(m => m.Make!.Name == c.Make && m.Name == c.Model);
                if (model is null) continue;
                context.VehicleConfigs.Add(new VehicleConfig
                {
                    ModelId = model.Id, YearFrom = c.From, YearTo = c.To, Trim = c.Trim, Engine = c.Engine, Fuel = c.Fuel,
                    Transmission = c.Transmission, Drivetrain = c.Drivetrain, Market = c.Market
                });
            }
            context.SaveChanges();
        }
    }
}
