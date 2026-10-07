# AuctionService

## Summary

Owns auction and item data — the system of record for auctions of whole vehicles and car parts, their
images (stored via `IImageStorage`) and the shipping containers they travel in. Exposes a REST API for
creating/reading/updating/deleting auctions, images and containers, publishes domain events over RabbitMQ whenever an
auction changes, and exposes a gRPC endpoint that BidService calls to fetch an auction it doesn't
yet have locally. Also consumes bid/auction-lifecycle events from BidService to keep `CurrentHighBid`,
`Winner`, `SoldAmount` and `Status` up to date.

## Infrastructure

.Net Web API
Postgres DB
Entity Framework ORM
Service Bus - RabbitMQ (WolverineFx)
gRPC server (auction lookups for BidService)

## Nuget Packages

Grpc.AspNetCore
Mapster
Microsoft.AspNetCore.Authentication.JwtBearer
Microsoft.EntityFrameworkCore.Design
Npgsql.EntityFrameworkCore.PostgreSQL
WolverineFx.EntityFrameworkCore
WolverineFx.Postgresql
WolverineFx.RabbitMQ
WolverineFx.RuntimeCompilation

## External (User)

- **CreateAuction** - Creates an Auction/Item (a whole **Vehicle** or a car **Part**). Validated by item type and pricing rules (see *Validation rules*). Emits **AuctionCreated**
- **UpdateAuction** - Partial update of item details, pricing, end time and container link. Rejected if the auction already has a bid (`CurrentHighBid > 0`), has ended (`Status != Live`) or the caller isn't the seller. Because of the "no edits after a bid" rule, `StartingPrice`, `ReservePrice`, `BidIncrement`, `ItemType` and `AuctionEnd` cannot change after the first bid. Emits **AuctionUpdated**
- **DeleteAuction** - Deletes an auction. Rejected if the auction already has a bid or the caller isn't the seller. Also deletes its uploaded image files. Emits **AuctionDeleted**
- **RelistAuction** - The seller relists an `Unsold` or `ReserveNotMet` auction: a new `Live` auction with the same item details, container and images (uploaded files are copied, external seed URLs reused), with new `StartingPrice`, `ReservePrice`, `BidIncrement` and `AuctionEnd` (same validation as create). The original auction is unchanged. Emits **AuctionCreated**
- **UploadImages** - Seller uploads one or more images (multipart). Max `ImageUpload:MaxFiles` (10) per item, `ImageUpload:MaxFileSizeMB` (5) each, extensions `ImageUpload:AllowedExtensions` (jpg, jpeg, png, webp). The real type is checked from the file signature (magic bytes); the stored file gets a generated unique name with the detected extension. The first image of an item becomes primary. Emits **AuctionUpdated**
- **DeleteImage** - Seller removes an image (and its file). If it was primary, the next image becomes primary. Emits **AuctionUpdated**
- **ReorderImages** - Seller sets the order of all images and (optionally) which one is primary (default: the first). Emits **AuctionUpdated**

- **CreateContainer** - Any logged-in user (policy `CanManageContainers`, defined once in `Validation/ContainerRules`; make it admin-only there later) creates a shipping container. `ContainerNumber` is normalized (spaces removed, upper case), must be 4 letters + 7 digits and unique (409 otherwise); `EstimatedArrivalDate` ≥ `DepartureDate`. Dates without a time zone are taken as UTC
- **UpdateContainer** - Creator only (403 otherwise). Replaces all fields, bumps `UpdatedAt` of the linked auctions (so SearchService's startup catch-up also sees the change) and emits **ContainerUpdated** with the ids of the linked auctions
- **DeleteContainer** - Creator only; 409 if any auction still uses the container

Any seller may link any existing container to their auction (`ContainerId` on create/update).

Image changes follow the same rule as other edits: only while the auction is `Live` and has no bids.

## Image storage

Behind `Services/IImageStorage` (`SaveAsync`, `DeleteAsync`, `CopyAsync`), selected by `ImageStorage:Provider`. The only implementation today is `LocalDiskImageStorage` (`Local`), which writes to `ImageStorage:LocalPath` (default `uploads/` under the content root; `/app/uploads` on the `auction_uploads` Docker volume) and stores the URL as `/images/{file}`. Files are served by the static file middleware under `ImageStorage:RequestPath` with only image content types, `Cache-Control: public, max-age=31536000, immutable` and `X-Content-Type-Options: nosniff`, and publicly through the Gateway at `/images/{file}`. DTOs and events turn stored paths into absolute URLs with `ImageStorage:PublicBaseUrl`. To add Azure Blob / S3, implement `IImageStorage` (returning absolute URLs) and register it for a new provider name in `Program.cs`.

## Queries handled

- **GetAuctions** - Gets all auctions, optionally filtered to `UpdatedAt > date` (used by SearchService to catch up on startup). Returns list of **AuctionDto** (never includes `ReservePrice`), ordered by Make/Model
- **GetContainers** - Lists containers ordered by estimated arrival, optional `searchTerm` (number, ports, countries; case-insensitive). Returns list of **ContainerDto** (with `AuctionCount`)
- **GetContainerById** - A container with the auctions shipped in it. Returns **ContainerDetailsDto**
- **GetAuctionById** - Gets an auction by ID. Returns **AuctionDto**; `ReservePrice` is included only when the authenticated caller is the seller

## Validation rules

Checked by `Validation/AuctionValidator` after create and after applying an update; failures return `400` ValidationProblem with errors keyed by camelCase field name.

| Rule | Applies to |
|---|---|
| `Make`, `Model`, `Description` required; `Year` 1886..next year | all |
| `Color` and `Mileage` required | Vehicle |
| `PartName`, `PartCategory`, `Condition` required; `Quantity` ≥ 1 (default 1); `YearTo` (optional) ≥ `Year` | Part |
| Part fields are cleared (null) | Vehicle |
| `StartingPrice` > 0, `BidIncrement` > 0, `ReservePrice` ≥ `StartingPrice` (omitted/0 reserve = `StartingPrice`, i.e. no hidden reserve) | all |
| `AuctionEnd` in the future (on create, or when an update changes it) | all |
| `ContainerId` (optional) must reference an existing container | all |
| Item location (optional): `Latitude` and `Longitude` together and valid (not 0,0) | all |

### Item location

`Item.LocationName`, `Latitude`, `Longitude` (nullable, migration `ItemLocation`) say where the item is, for "parts within X km". On create, a request without a location gets the seller's primary location from the access token (claims `location_name`, `location_lat`, `location_lon`, issued by IdentityService). Relist copies it; update changes it only when sent. It is part of `AuctionDto` and of the `AuctionCreated` / `AuctionUpdated` snapshot, so SearchService indexes it.

## Seed data

When the database is empty (and the environment isn't `Test`) `DbInitializer` seeds 2 containers (`MSCU1234567` Jebel Ali → Nhava Sheva, in transit; `MAEU7654321` Yokohama → Durban, loading), 10 vehicles and 6 parts (4 of them linked to the containers), each with one `ItemImage` using an absolute external URL (seed data only). After the app has started it publishes **AuctionCreated** for every seeded auction so BidService schedules their end and SearchService indexes them.

Each demo item has a location spread over India, the USA and Nigeria (five around Hyderabad at 0–20 km from each other to try the radius chips; Bangalore, Delhi, Mumbai, New York, Los Angeles, Houston, Chicago, Lagos, Ikeja, Abuja, Port Harcourt). A database seeded before item locations existed gets these locations filled in once at startup, followed by **AuctionUpdated** for those items.

## Events emitted

- **AuctionCreated** - When an auction is created, in response to CreateAuction (and for seed data, see above)
- **AuctionUpdated** - When an auction is updated, in response to UpdateAuction
- **AuctionDeleted** - When an auction is deleted, in response to DeleteAuction
- **ContainerUpdated** - When a container is updated, in response to UpdateContainer (exchange `container-updated`)

All of them are published via the Wolverine EF Core outbox (`IDbContextOutbox<AuctionDbContext>`) in the same transaction as the DB write, to fanout exchanges (`auction-created`, `auction-updated`, `auction-deleted`).

## Events consumed

- **BidService.BidPlaced** - Updates `CurrentHighBid` when the incoming bid is `Accepted`/`AcceptedBelowReserve` and higher than the current value
- **BidService.AuctionFinished** - Applies the end status idempotently (`Entities/AuctionStatusRules`): `Live` → any end status, `ReserveNotMet` → `Sold`/`Unsold`, anything else is ignored (so duplicates and late messages are harmless). Sets `Winner`/`SoldAmount` when `Sold`, `EndReason`, and `SellerDecisionDeadline` while `ReserveNotMet`. Messages without `Status` (older publishers) map to `Sold` if `ItemSold`, otherwise `Unsold`

- **BidService.AuctionExtended** - Anti-sniping moved the end: sets `AuctionEnd` (only later, only while Live)

Also listens on `wolverine-dead-letter-queue`: a `DeadLetterHandler` logs any `AuctionCreated` message that lands there (test/demo hook, not part of normal flow).

## gRPC

**GetAuction(auctionId) → GrpcAuctionResponse** - Returns `{ id, auctionEnd, seller, reservePrice, finished, startingPrice, bidIncrement, status, sellerDecisionDeadline }` for an auction (internal: the reserve is needed by BidService). Called by BidService when it receives a bid for an auction it doesn't have in its own read model yet (e.g. BidService restarted before consuming `AuctionCreated`). Returns `NOT_FOUND` status if the auction doesn't exist.

## API Endpoints

|POST|api/auctions|Create auction|Auth|
|---|---|---|---|
|PUT|api/auctions/:id|Update auction (owner + no bids only)|Auth|
|DELETE|api/auctions/:id|Delete auction (owner + no bids only)|Auth|
|GET|api/auctions|Get auctions (optional `?date=` filter)|Anon|
|GET|api/auctions/:id|Get auction by id (reserve only for the seller)|Anon|
|POST|api/auctions/test|Echoes the authenticated username (auth smoke-test endpoint)|Auth|
|POST|api/auctions/:id/relist|Relist an ended, unsold auction: `{ startingPrice, reservePrice?, bidIncrement, auctionEnd }` → 201 **AuctionDto**|Auth (seller)|
|POST|api/auctions/:id/images|Upload images (multipart/form-data, one or more files) → list of `ItemImageDto`|Auth (seller)|
|DELETE|api/auctions/:id/images/:imageId|Delete an image|Auth (seller)|
|PUT|api/auctions/:id/images/order|Reorder + set primary: `{ imageIds: string[], primaryImageId?: string }` → list of `ItemImageDto`|Auth (seller)|
|GET|images/:fileName|Uploaded image file (static)|Anon|
|GET|api/containers|List containers (optional `?searchTerm=`)|Anon|
|GET|api/containers/:id|Container with its auctions|Anon|
|POST|api/containers|Create container|Auth (`CanManageContainers`)|
|PUT|api/containers/:id|Update container|Auth (creator)|
|DELETE|api/containers/:id|Delete container (only if unused)|Auth (creator)|

Auth is JWT Bearer, validated against IdentityService (`Authority` = IdentityServiceUrl), with `NameClaimType` mapped to the `username` claim. Enums are accepted and returned as strings (`JsonStringEnumConverter`; numbers are accepted too).

## Configuration

| Key | Default | Meaning |
|---|---|---|
| `ImageStorage:PublicBaseUrl` | `http://localhost:6001` | Public origin (the Gateway) prepended to stored image paths |
| `ImageStorage:RequestPath` | `/images` | Path images are served under |
| `ImageStorage:Provider` | `Local` | Storage implementation (`Local` = `LocalDiskImageStorage`) |
| `ImageStorage:LocalPath` | `uploads` | Folder for uploaded files (relative to the content root) |
| `ImageUpload:MaxFiles` | `10` | Max images per item |
| `ImageUpload:MaxFileSizeMB` | `5` | Max size of one image |
| `ImageUpload:AllowedExtensions` | `.jpg .jpeg .png .webp` | Accepted image types |

## Models

**Auction.cs**

| Property Name   | Property Type                | Default Value      |
| --------------- | ----------------------------- | ------------------- |
| Id              | string (Guid string)          | Guid.NewGuid().ToString() |
| StartingPrice   | int                            | 1 (existing rows migrated to 1) |
| ReservePrice    | int (hidden from buyers)       | 1 (= StartingPrice means no hidden reserve) |
| BidIncrement    | int                            | 1 (existing rows migrated to 1) |
| Seller          | string (username from claim)   |                      |
| Winner?         | string (username of winner)    |                      |
| SoldAmount?     | int                             |                      |
| CurrentHighBid? | int                             |                      |
| CreatedAt       | DateTime                       | DateTime.UtcNow      |
| UpdatedAt       | DateTime                       | DateTime.UtcNow      |
| AuctionEnd      | DateTime                       |                      |
| Status          | Status                          | Status.Live (default enum value) |
| EndReason?      | EndReason                       |                      |
| SellerDecisionDeadline? | DateTime (end of the ReserveNotMet grace period) |  |
| Item            | Item                            |                      |

Helpers: `HasReservePrice()` (= `ReservePrice > StartingPrice`), `IsReserveMet()` (= `CurrentHighBid >= ReservePrice`).

**Item.cs** (table `Items`)

| Property Name | Property Type                    | Default Value |
| ------------- | ---------------------------------- | ------------- |
| Id            | string (same as the Auction Id)     |               |
| ItemType      | ItemType                            | Vehicle (existing rows migrated to Vehicle) |
| Make          | string (Part: make it fits)         |               |
| Model         | string (Part: model it fits)        |               |
| Year          | int (Part: first year it fits)      |               |
| YearTo?       | int (Part only: last year it fits)  |               |
| Color?        | string (required for Vehicle)       |               |
| Mileage?      | int (required for Vehicle)          |               |
| Description   | string                              |               |
| PartName?     | string (Part only, required)        |               |
| PartNumber?   | string (Part only, OEM number)      |               |
| PartCategory? | PartCategory (Part only, required)  |               |
| Condition?    | ItemCondition (Part only, required) |               |
| Quantity?     | int (Part only, ≥ 1)                | 1 for parts   |
| ContainerId?  | string (FK → Containers, restrict delete) |         |
| Images        | List&lt;ItemImage&gt;               |               |
| Auction       | Auction? (related to **Auction**)   |               |

**ItemImage.cs** (table `ItemImages`, cascade-deleted with the item)

| Property Name | Property Type | Default Value |
|---|---|---|
| Id | string (Guid string) | Guid.NewGuid().ToString() |
| ItemId | string (FK → Items) | |
| Url | string: `/images/{file}` for uploads, absolute URL for seed/migrated data | |
| FileName | string | |
| SortOrder | int | |
| IsPrimary | bool (exactly one per item) | |
| CreatedAt | DateTime | DateTime.UtcNow |

**Container.cs** (table `Containers`, unique index on `ContainerNumber`)

| Property Name | Property Type | Default Value |
|---|---|---|
| Id | string (Guid string) | Guid.NewGuid().ToString() |
| ContainerNumber | string (4 letters + 7 digits, upper case) | |
| OriginPort / OriginCountry | string | |
| DestinationPort / DestinationCountry | string | |
| DepartureDate | DateTime | |
| EstimatedArrivalDate | DateTime (≥ DepartureDate) | |
| ShippingStatus | ShippingStatus | Loading |
| CreatedBy | string (username) | |
| CreatedAt / UpdatedAt | DateTime | DateTime.UtcNow |

**Enums** (stored as integers)

| Enum | Values |
|---|---|
| Status | `Live` (0), `Sold` (1, was `Finished`), `ReserveNotMet` (2, ended, waiting for the seller), `Unsold` (3) |
| ItemType | `Vehicle`, `Part` |
| PartCategory | `Body`, `Engine`, `Transmission`, `Electrical`, `Suspension`, `Brakes`, `Interior`, `WheelsAndTyres`, `Lighting`, `Other` |
| ItemCondition | `New`, `Used`, `Refurbished` |
| ShippingStatus | `Loading`, `InTransit`, `Arrived`, `Delivered` |
| EndReason | `TimeEnded`, `SellerAccepted`, `SellerAcceptedAfterEnd`, `GracePeriodExpired` |

Migration `PartsContainersImagesPricing`: adds the columns/tables above; copies each existing `Items.ImageUrl` into a primary `ItemImages` row before dropping the column; sets `ReservePrice = GREATEST(ReservePrice, StartingPrice)`; converts old `ReserveNotMet` auctions (which had no grace period) to `Sold` (if they have a winner) or `Unsold`.

## DTOs

**AuctionDto.cs**

| Property Name   | Property Type | Notes |
| ---------------- | -------------- | -------------- |
| Id                | string         |                |
| StartingPrice     | int            |                |
| BidIncrement      | int            |                |
| ReservePrice?     | int            | **only for the seller**; omitted otherwise |
| ReserveMet        | bool           | `CurrentHighBid >= ReservePrice` |
| Seller            | string         |                |
| Winner?           | string         |                |
| SoldAmount?       | int            |                |
| CurrentHighBid?   | int            |                |
| CreatedAt / UpdatedAt / AuctionEnd | DateTime |      |
| Status            | string         | Live / Sold / ReserveNotMet / Unsold |
| EndReason?        | string         |                |
| SellerDecisionDeadline? | DateTime |                |
| ItemType          | string         | Vehicle / Part |
| Make, Model, Description | string  |                |
| Year, YearTo?     | int            |                |
| Color?, Mileage?  | string, int    |                |
| PartName?, PartNumber?, PartCategory?, Condition? | string | |
| Quantity?         | int            |                |
| ContainerId?      | string         |                |
| Container?        | ContainerInfo  | see Contracts  |
| ImageUrl          | string         | primary image URL (empty if none) |
| Images            | string[]       | image URLs, primary first |
| ImageItems        | {id, url, sortOrder, isPrimary}[] | for the seller's image manager |

**ContainerDto.cs** - `Id`, `ContainerNumber`, `OriginPort`, `OriginCountry`, `DestinationPort`, `DestinationCountry`, `DepartureDate`, `EstimatedArrivalDate`, `ShippingStatus` (string), `CreatedBy`, `CreatedAt`, `UpdatedAt`, `AuctionCount`. **ContainerDetailsDto** adds `Auctions` (list of **AuctionDto**, no reserve).

**SaveContainerDto.cs** (POST/PUT body) - `ContainerNumber`, `OriginPort`, `OriginCountry`, `DestinationPort`, `DestinationCountry`, `DepartureDate`, `EstimatedArrivalDate`, `ShippingStatus` (default `Loading`).

**CreateAuctionDto.cs**

|Property Name|Property Type|Default Value|
|---|---|---|
|ItemType|ItemType|Vehicle|
|Make, Model, Description|string||
|Year|int||
|YearTo?|int||
|Color?|string||
|Mileage?|int||
|PartName?, PartNumber?|string||
|PartCategory?|PartCategory||
|Condition?|ItemCondition||
|Quantity?|int|1 for parts|
|ContainerId?|string||
|StartingPrice|int||
|ReservePrice?|int|StartingPrice|
|BidIncrement|int||
|AuctionEnd|DateTime||

**RelistAuctionDto.cs** - `StartingPrice`, `ReservePrice?` (default `StartingPrice`), `BidIncrement`, `AuctionEnd`.

**UpdateAuctionDto.cs** - same fields as CreateAuctionDto, all optional. `null` = unchanged; `""` clears `PartNumber`/`ContainerId`/`Color`, `0` clears `YearTo` (and a part's `Mileage`); `ReservePrice = 0` resets the reserve to the starting price. Switching to `Vehicle` clears the part fields.

## Event Emitted Types

**AuctionCreated** / **AuctionUpdated** - both derive from `Contracts.AuctionSnapshot` (the full auction state):

|Property Name|Property Type|Notes|
|---|---|---|
|Id|string||
|ReservePrice|int|internal: used by BidService only, never exposed by consumers|
|StartingPrice, BidIncrement|int||
|ReserveMet|bool||
|Seller|string||
|Winner?|string||
|SoldAmount?, CurrentHighBid?|int||
|CreatedAt, UpdatedAt, AuctionEnd|DateTime||
|Status|string||
|EndReason?|string||
|SellerDecisionDeadline?|DateTime||
|ItemType|string||
|Make, Model, Description|string||
|Year, YearTo?|int||
|Color?, Mileage?|string, int||
|PartName?, PartNumber?, PartCategory?, Condition?|string||
|Quantity?|int||
|Container?|ContainerInfo|`{ id, containerNumber, originPort, originCountry, destinationPort, destinationCountry, departureDate, estimatedArrivalDate, shippingStatus }`|
|ImageUrl|string|primary image URL|
|Images|string[]|image URLs, primary first|

**AuctionDeleted**

|Property Name|Property Type|Default Value|
|---|---|---|
|Id|string||

**ContainerUpdated** - all `ContainerInfo` fields (`Id`, `ContainerNumber`, `OriginPort`, `OriginCountry`, `DestinationPort`, `DestinationCountry`, `DepartureDate`, `EstimatedArrivalDate`, `ShippingStatus`) plus `AuctionIds` (string[], auctions linked to the container)

## Event Consumed Types

**BidService.BidPlaced**

|Property Name|Property Type|Default Value|
|---|---|---|
|Id|string||
|AuctionId|string||
|Bidder|string||
|BidTime|DateTime||
|Amount|int||
|BidStatus|string||

**BidService.AuctionFinished**

|Property Name|Property Type|Default Value|
|---|---|---|
|ItemSold|bool||
|AuctionId|string||
|Winner?|string||
|Seller|string||
|Amount?|int||
|Status?|string (Sold / ReserveNotMet / Unsold)||
|EndReason?|string||
|SellerDecisionDeadline?|DateTime||
|BidId?|string||
