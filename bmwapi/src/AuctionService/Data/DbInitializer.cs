using AuctionService.Entities;
using AuctionService.RequestHelpers;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace AuctionService.Data;

public class DbInitializer
{
    public static void InitDb(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var seededIds = SeedData(scope.ServiceProvider.GetRequiredService<AuctionDbContext>(), app);

        // Tell the other services (BidService schedules each auction's end, SearchService indexes it)
        // about the seed data once Wolverine is running.
        if (seededIds.Count > 0)
        {
            app.Lifetime.ApplicationStarted.Register(() => _ = PublishSeededAuctions(app, seededIds));
        }
    }

    private static List<string> SeedData(AuctionDbContext context, WebApplication app)
    {
        context.Database.Migrate();

        if (context.Auctions.Any() || app.Environment.IsEnvironment("Test"))
        {
            Console.WriteLine("Database Already Exists");
            return [];
        }

        var now = DateTime.UtcNow;

        var mscu = new Container
        {
            Id = "mscu1234567",
            ContainerNumber = "MSCU1234567",
            OriginPort = "Jebel Ali",
            OriginCountry = "United Arab Emirates",
            DestinationPort = "Nhava Sheva",
            DestinationCountry = "India",
            DepartureDate = now.AddDays(-5),
            EstimatedArrivalDate = now.AddDays(9),
            ShippingStatus = ShippingStatus.InTransit,
            CreatedBy = "bob"
        };

        var maeu = new Container
        {
            Id = "maeu7654321",
            ContainerNumber = "MAEU7654321",
            OriginPort = "Yokohama",
            OriginCountry = "Japan",
            DestinationPort = "Durban",
            DestinationCountry = "South Africa",
            DepartureDate = now.AddDays(3),
            EstimatedArrivalDate = now.AddDays(28),
            ShippingStatus = ShippingStatus.Loading,
            CreatedBy = "alice"
        };

        context.Containers.AddRange(mscu, maeu);

        var auctions = new List<Auction>()
        {
            // 1 Ford GT
            Vehicle("ford-gt", "bob", now.AddDays(10), 15000, 20000, 500,
                "Ford", "GT", 2020, "White", 50000,
                "https://cdn.pixabay.com/photo/2016/05/06/16/32/car-1376190_960_720.jpg",
                "A modern reincarnation of Ford's legendary Le Mans racer, this GT pairs a twin-turbo V6 with a carbon-fiber body for supercar performance. Meticulously maintained, it turns heads wherever it goes."),
            // 2 Bugatti Veyron
            Vehicle("bugatti-veyron", "alice", now.AddDays(60), 60000, 90000, 1000,
                "Bugatti", "Veyron", 2018, "Black", 15035,
                "https://cdn.pixabay.com/photo/2012/05/29/00/43/car-49278_960_720.jpg",
                "An engineering marvel with a quad-turbo W16 engine delivering blistering acceleration and once-impossible top speeds. Garage-kept and lightly driven, this Veyron is a rare chance to own a true automotive icon."),
            // 3 Ford mustang (no hidden reserve)
            Vehicle("ford-mustang", "bob", now.AddDays(4), 5000, 5000, 250,
                "Ford", "Mustang", 2023, "Black", 65125,
                "https://cdn.pixabay.com/photo/2012/11/02/13/02/car-63930_960_720.jpg",
                "A modern Mustang with the unmistakable growl of a naturally aspirated V8. Well cared for and ready to deliver the raw, engaging driving experience Mustang fans expect."),
            // 4 Mercedes SLK (ended without bids)
            Vehicle("mercedes-slk", "tom", now.AddDays(-10), 30000, 50000, 1000,
                "Mercedes", "SLK", 2020, "Silver", 15001,
                "https://cdn.pixabay.com/photo/2016/04/17/22/10/mercedes-benz-1335674_960_720.png",
                "A sleek SLK roadster that blends German engineering with timeless open-top style. The retractable hardtop and smooth engine make it an effortless weekend cruiser with low mileage for its age.",
                Status.Unsold),
            // 5 BMW X1
            Vehicle("bmw-x1", "alice", now.AddDays(30), 10000, 20000, 500,
                "BMW", "X1", 2017, "White", 90000,
                "https://cdn.pixabay.com/photo/2017/08/31/05/47/bmw-2699538_960_720.jpg",
                "A practical BMW compact SUV that balances everyday comfort with the brand's signature driving feel. Higher mileage but well maintained, making it a solid choice for a dependable daily driver."),
            // 6 Ferrari spider
            Vehicle("ferrari-spider", "bob", now.AddDays(45), 15000, 20000, 500,
                "Ferrari", "Spider", 2015, "Red", 50000,
                "https://cdn.pixabay.com/photo/2017/11/09/01/49/ferrari-458-spider-2932191_960_720.jpg",
                "A drop-top Ferrari spider that delivers open-air thrills alongside a screaming naturally aspirated V8. A head-turning weekend car with the kind of exhaust note that never gets old."),
            // 7 Ferrari F-430
            Vehicle("ferrari-f430", "alice", now.AddDays(13), 100000, 150000, 2500,
                "Ferrari", "F-430", 2022, "Red", 5000,
                "https://cdn.pixabay.com/photo/2017/11/08/14/39/ferrari-f430-2930661_960_720.jpg",
                "A pristine, low-mileage F-430 with a high-revving V8 and razor-sharp handling. Barely broken in, this is as close to a showroom Ferrari as you'll find at auction."),
            // 8 Audi R8 (no hidden reserve)
            Vehicle("audi-r8", "bob", now.AddDays(19), 50000, 50000, 1000,
                "Audi", "R8", 2021, "White", 10050,
                "https://cdn.pixabay.com/photo/2019/12/26/20/50/audi-r8-4721217_960_720.jpg",
                "A mid-engine Audi supercar that combines everyday usability with genuine performance credentials. Low mileage and a clean history make this R8 a rare find at this price point."),
            // 9 Audi TT
            Vehicle("audi-tt", "tom", now.AddDays(20), 12000, 20000, 500,
                "Audi", "TT", 2020, "Black", 25400,
                "https://cdn.pixabay.com/photo/2016/09/01/15/06/audi-1636320_960_720.jpg",
                "A sharp, compact Audi coupe with distinctive styling and confident handling. Well within its service intervals, it offers a fun and stylish alternative to more common sports coupes."),
            // 10 Ford Model T
            Vehicle("ford-model-t", "bob", now.AddDays(48), 8000, 20000, 250,
                "Ford", "Model T", 1938, "Rust", 150150,
                "https://cdn.pixabay.com/photo/2017/08/02/19/47/vintage-2573090_960_720.jpg",
                "A genuine piece of automotive history, this Model T has survived decades and racked up plenty of miles along the way. A patinated project car for the collector who values character over polish."),

            // Parts (images: Wikimedia Commons, see README for credits)
            Part("toyota-hilux-front-bumper", "bob", now.AddDays(7), 300, 500, 25,
                "Front bumper", "52119-0K954", PartCategory.Body, ItemCondition.Used, 1,
                "Toyota", "Hilux", 2016, 2020, mscu,
                "https://thumb.wikimedia.org/wikipedia/commons/thumb/2/22/Broken_car_front_bumper.jpg/960px-Broken_car_front_bumper.jpg",
                "Genuine front bumper cover removed from a 2018 Hilux. Light scuffs and one cracked mounting tab on the left side; ready for paint."),
            Part("honda-civic-alternator", "alice", now.AddDays(5), 120, 200, 10,
                "Alternator", "31100-R1A-A01", PartCategory.Electrical, ItemCondition.Refurbished, 1,
                "Honda", "Civic", 2012, 2015, null,
                "https://thumb.wikimedia.org/wikipedia/commons/thumb/f/ff/Car_alternator_on_a_workbench_surrounded_by_various_auto_parts_in_a_garage_setting.jpg/960px-Car_alternator_on_a_workbench_surrounded_by_various_auto_parts_in_a_garage_setting.jpg",
                "Professionally rebuilt 1.8L alternator with new bearings, brushes and regulator. Bench tested at 95A output."),
            Part("bmw-3-series-brake-discs", "bob", now.AddDays(3), 150, 150, 10,
                "Front brake discs (pair)", "34116860912", PartCategory.Brakes, ItemCondition.New, 2,
                "BMW", "3 Series", 2019, 2023, maeu,
                "https://upload.wikimedia.org/wikipedia/commons/c/c4/Ventilated_2-Piece_Floating_Disc_Curved_Drilled_%26_Slotted_Brake_Rotor.jpg",
                "New ventilated, drilled and slotted front discs for the G20 3 Series, 330 mm. Sold as a pair, still boxed."),
            Part("ford-ranger-led-headlight", "alice", now.AddHours(2), 250, 400, 20,
                "LED headlight (left)", "JB3B-13W030-AE", PartCategory.Lighting, ItemCondition.New, 1,
                "Ford", "Ranger", 2019, 2022, mscu,
                "https://upload.wikimedia.org/wikipedia/commons/2/29/Car_headlamp.png",
                "New left-hand LED headlight assembly with daytime running light. Plug and play, no coding required."),
            Part("audi-a4-alloy-wheels", "bob", now.AddDays(10), 600, 900, 50,
                "18\" alloy wheels (set of 4)", "8W0601025", PartCategory.WheelsAndTyres, ItemCondition.Used, 4,
                "Audi", "A4", 2016, 2020, maeu,
                "https://thumb.wikimedia.org/wikipedia/commons/thumb/b/bb/Wheel%2C_IAA_2017%2C_Frankfurt_%281Y7A3040%29.jpg/960px-Wheel%2C_IAA_2017%2C_Frankfurt_%281Y7A3040%29.jpg",
                "Set of four 18\" 5-spoke alloys, 8J x 18 ET40. Minor kerb marks on two wheels, no buckles. Tyres not included."),
            Part("vw-golf-gearbox", "alice", now.AddDays(14), 800, 1200, 50,
                "6-speed manual gearbox", "02Q300045", PartCategory.Transmission, ItemCondition.Refurbished, 1,
                "Volkswagen", "Golf", 2013, 2017, null,
                "https://thumb.wikimedia.org/wikipedia/commons/thumb/2/2e/Manual_transmission_in_2nd-gear.jpg/960px-Manual_transmission_in_2nd-gear.jpg",
                "Rebuilt 02Q 6-speed manual for the 2.0 TDI Golf Mk7. New synchros and bearings, 6 month warranty.")
        };

        context.Auctions.AddRange(auctions);

        context.SaveChanges();

        return auctions.Select(x => x.Id).ToList();
    }

    private static Auction Vehicle(string id, string seller, DateTime end, int startingPrice, int reservePrice,
        int bidIncrement, string make, string model, int year, string color, int mileage, string imageUrl,
        string description, Status status = Status.Live) => new()
    {
        Id = id,
        Status = status,
        EndReason = status == Status.Live ? null : EndReason.TimeEnded,
        StartingPrice = startingPrice,
        ReservePrice = reservePrice,
        BidIncrement = bidIncrement,
        Seller = seller,
        AuctionEnd = end,
        Item = new Item
        {
            Id = id,
            ItemType = ItemType.Vehicle,
            Make = make,
            Model = model,
            Color = color,
            Mileage = mileage,
            Year = year,
            Description = description,
            Images = [SeedImage(id, imageUrl)]
        }
    };

    private static Auction Part(string id, string seller, DateTime end, int startingPrice, int reservePrice,
        int bidIncrement, string partName, string partNumber, PartCategory category, ItemCondition condition,
        int quantity, string make, string model, int year, int yearTo, Container? container, string imageUrl,
        string description) => new()
    {
        Id = id,
        Status = Status.Live,
        StartingPrice = startingPrice,
        ReservePrice = reservePrice,
        BidIncrement = bidIncrement,
        Seller = seller,
        AuctionEnd = end,
        Item = new Item
        {
            Id = id,
            ItemType = ItemType.Part,
            PartName = partName,
            PartNumber = partNumber,
            PartCategory = category,
            Condition = condition,
            Quantity = quantity,
            Make = make,
            Model = model,
            Year = year,
            YearTo = yearTo,
            Description = description,
            Container = container,
            Images = [SeedImage(id, imageUrl)]
        }
    };

    // Seed data only: absolute external URLs are allowed here; uploads are always stored locally.
    private static ItemImage SeedImage(string itemId, string url) => new()
    {
        ItemId = itemId,
        Url = url,
        FileName = Uri.UnescapeDataString(url[(url.LastIndexOf('/') + 1)..]),
        SortOrder = 0,
        IsPrimary = true
    };

    private static async Task PublishSeededAuctions(WebApplication app, List<string> ids)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
            var mapper = scope.ServiceProvider.GetRequiredService<AuctionMapper>();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            var auctions = await context.Auctions.AsNoTracking().WithDetails()
                .Where(x => ids.Contains(x.Id)).ToListAsync();

            foreach (var auction in auctions)
            {
                await bus.PublishAsync(mapper.ToCreated(auction));
            }

            Console.WriteLine($"Published AuctionCreated for {auctions.Count} seeded auctions");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to publish seeded auctions: {e.Message}");
        }
    }
}