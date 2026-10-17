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
- **UploadImages** - Seller uploads one or more images (multipart, optional `angle` per file for vehicle photos). Each image is processed by `Services/ImageProcessor` (SkiaSharp): EXIF/GPS removed, orientation applied, longest side ≤ 1600 px, plus a 400 px thumbnail (`ThumbnailUrl`). Draft or Live without bids: add/delete/reorder; after the first bid: **add only**. Max `ImageUpload:MaxFiles` (20) per item, `ImageUpload:MaxFileSizeMB` (5) each, extensions `ImageUpload:AllowedExtensions` (jpg, jpeg, png, webp). The real type is checked from the file signature (magic bytes); the stored file gets a generated unique name with the detected extension. The first image of an item becomes primary. Emits **AuctionUpdated**
- **DeleteImage** - Seller removes an image (and its file). If it was primary, the next image becomes primary. Emits **AuctionUpdated**
- **ReorderImages** - Seller sets the order of all images and (optionally) which one is primary (default: the first). Emits **AuctionUpdated**

- **CreateContainer** - Any logged-in user (policy `CanManageContainers`, defined once in `Validation/ContainerRules`; make it admin-only there later) creates a shipping container. `ContainerNumber` is normalized (spaces removed, upper case), must be 4 letters + 7 digits and unique (409 otherwise); `EstimatedArrivalDate` ≥ `DepartureDate`. Dates without a time zone are taken as UTC
- **UpdateContainer** - Creator only (403 otherwise). Replaces all fields, bumps `UpdatedAt` of the linked auctions (so SearchService's startup catch-up also sees the change) and emits **ContainerUpdated** with the ids of the linked auctions
- **DeleteContainer** - Creator only; 409 if any auction still uses the container

Any seller may link any existing container to their auction (`ContainerId` on create/update).

Image changes follow the same rule as other edits: only while the auction is `Live` and has no bids.

## Image storage

Behind `Services/IImageStorage` (`SaveAsync`, `DeleteAsync`, `CopyAsync`), selected by `ImageStorage:Provider`:
`GoogleCloudStorage` (`GoogleCloudImageStorage`) uploads to the bucket `ImageStorage:Gcs:Bucket` under
`ImageStorage:Gcs:ObjectPrefix` and returns absolute URLs (`https://storage.googleapis.com/{bucket}/…` or
`ImageStorage:Gcs:PublicBaseUrl`); credentials come **only** from the environment (`GOOGLE_APPLICATION_CREDENTIALS`
or the workload identity), never from settings. `ListingImageService` runs the processing and stores the full image
and its thumbnail. `LocalDiskImageStorage` (`Local`, the development default) writes to `ImageStorage:LocalPath` (default `uploads/` under the content root; `/app/uploads` on the `auction_uploads` Docker volume) and stores the URL as `/images/{file}`. Files are served by the static file middleware under `ImageStorage:RequestPath` with only image content types, `Cache-Control: public, max-age=31536000, immutable` and `X-Content-Type-Options: nosniff`, and publicly through the Gateway at `/images/{file}`. DTOs and events turn stored paths into absolute URLs with `ImageStorage:PublicBaseUrl`. To add Azure Blob / S3, implement `IImageStorage` (returning absolute URLs) and register it for a new provider name in `Program.cs`.

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
- **OrderStatusChanged** - When an order moves to a tracking step or the buyer chooses the delivery method (exchange `order-status-changed`)
- **CustomOrderMatched** - When a new or relisted listing matches a buyer's open custom order (exchange `custom-order-matched`, see *Custom orders*)

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
|GET|api/geo/reverse?lat=&lon=|City, country and default radius for the phone's position → `{ latitude, longitude, city, countryCode, supported, radiusValue, radiusUnit, radiusKm }` (60 mi in the USA, 60 km elsewhere)|Auth|
|POST|api/partrequests|Create a custom order `{ searchText?, partName?, partNumber?, make?, model?, year?, specs?, maxBudget?, locationName?, countryCode?, latitude?, longitude?, radiusKm? }` (one of partName / partNumber / searchText required) → 201, status `Open`|Auth|
|GET|api/partrequests|The signed-in user's custom orders, newest first|Auth|
|GET|api/partrequests/:id|One of the user's custom orders|Auth (owner)|
|POST|api/partrequests/:id/close|Close a custom order (no more notifications)|Auth (owner)|
|POST|api/listings|Start a draft: `{ kind: CommonPart \| VehicleSpecificPart \| Vehicle, partTypeId? }` → 201 **AuctionDto** (`status: Draft`)|Auth|
|GET|api/listings/:id|A listing (drafts / cancelled only for the seller); includes `canEdit`|Auth|
|PUT|api/listings/:id|Autosave a draft or edit a Live listing without bids (fitments, part numbers, specs, details, vehicle, location, `deliveryOptions: [Pickup, Shipping]` (at least one), `shippingFee` (0 / `freeShipping: true` = free; required with Shipping, as is `sizeClass`), `pickupAddress` (private), price, `saleType: Auction \| FixedPrice`, `auctionEnd` – the seller-chosen end, UTC, 1 hour to 30 days ahead; older apps send `durationDays` instead). A listing with units sold can't switch back to Auction|Auth (seller)|
|POST|api/listings/:id/publish|Validate (`ListingRules.ValidateForPublish`) → `Live` + **AuctionCreated**; 400 ValidationProblem keyed by step field (`partType`, `fitments`, `specs`, `photos`, `description`, `auctionEnd`, …). Auctions end at `auctionEnd` (or now + `durationDays`); FixedPrice listings get no end (`9999-12-31`) and stay Live until sold out or cancelled. Stores the seller's display name (`name` claim) as `sellerName`|Auth (seller)|
|POST|api/listings/:id/buy|FixedPrice only: `{ quantity }` (1 … `quantityAvailable`) → **OrderDto** (`saleType: FixedPrice`, `quantity`, `finalTotal` = price × quantity, first step "Bought"); one order per purchase; the last unit makes the listing `Sold`. Publishes **AuctionUpdated** (units left) and **OrderStatusChanged** to the seller. 400 own listing / auction / sold out / too many; 409 when another buyer took the units first|Auth (buyer)|
|POST|api/listings/:id/notes|`{ text }` – append a dated note to the description (allowed after bids; no contact details)|Auth (seller)|
|POST|api/listings/:id/cancel|Draft → deleted; Live without bids → `Cancelled` (**AuctionDeleted** for the index); with bids → 400|Auth (seller)|
|POST|api/listings/:id/relist|An `Unsold` listing → a new Live copy (same data and photos, new end = now + duration)|Auth (seller)|
|GET|api/listings/mine|My Listings with `group`: Draft / Live / Ended – no bids / Sold / Completed / Cancelled|Auth|
|GET|api/listings/last-fitment?exceptId=|Fitments of the seller's last part listing ("Copy fitment")|Auth|
|GET|api/catalog/categories?commonOnly=, part-types?q=&fitmentMode=&commonOnly=, part-types/:id|Master data (part types with synonyms and spec fields). `commonOnly=true`: only part types that fit any vehicle (Universal / SizeBased) and only the categories that contain some – the "Common part" picker|Anon|
|GET|api/catalog/makes?q=&country=, makes/:id/models, models/:id/configs, popular?country=|Makes/models (approved, plus the caller's own pending ones)|Anon|
|POST|api/catalog/makes/request, models/request|Seller requests a missing make/model (usable on their listing at once, `Pending` for others)|Auth|
|GET/POST|api/catalog/pending, makes/:id/approve, models/:id/approve|Approve requested makes/models|Auth (`Admin:Usernames`)|
|GET|api/catalog/dictionary|Part types, synonyms, makes, models and aliases for SearchService's query parser|Anon|
|GET|api/orders, api/orders/:id|The caller's orders as buyer or seller, with `tracking` steps, `statusLabel` and the caller's next `actions` (other party's contact only after the sale)|Auth|
|POST|api/orders/:id/delivery-method|Buyer, while Sold: `{ method: Pickup \| Shipping, addressId? , address?: { name, phone, line1, line2?, city, state?, placeId?, postcode, countryCode }, saveAddress = true }`. Shipping needs an address-book entry or a valid new address (postcode format per country; Nigeria optional) **in the seller's country** (400 "ships within … only"); a new address is saved to the address book. Shipping-only orders start with method Shipping and `needsShippingAddress: true`|Auth (buyer)|
|POST|api/orders/:id/status|`{ status, courier?, trackingNumber? }` – seller: ReadyForPickup / PickedUp (Pickup) or Shipped (Shipping, needs the address; courier and tracking optional), forward only, may skip; buyer: Completed (Pickup) or Delivered (Shipping – also completes the order), any time. Older names Dispatched / HandedOver are accepted. Order DTO: `partPrice`, `winningBid`, `shippingFee`, `total`, `shipTo`, `pickupAddress` (exact address, buyer after the sale), `needsShippingAddress`|Auth (seller / buyer)|
|GET/POST|api/shipping-addresses|The caller's address book (default first) / add one `{ …address, isDefault? }` (the first is the default; max 20)|Auth|
|POST|api/media?purpose=chat\|review|1–5 photos (multipart) for chat messages / reviews: same checks and storage as listing photos (content, 5 MB, re-encoded without EXIF, thumbnail) → `[{ id, url, thumbnailUrl }]`; recorded with the uploader (MediaFiles)|Auth|
|PUT/DELETE|api/shipping-addresses/:id; POST …/:id/default|Edit, delete (the next one becomes the default), make default. Orders keep their own copy|Auth (owner)|
|POST|api/orders/:id/handed-over, api/orders/:id/received|Older apps: the handover step of the method; buyer → `Completed`|Auth (seller / buyer)|
|GET/POST/DELETE|api/garage, api/garage/:id|My Garage vehicles `{ make, model, year, … }`|Auth|
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
| `ImageStorage:Provider` | `Local` | `Local` (`LocalDiskImageStorage`) or `GoogleCloudStorage` |
| `ImageStorage:LocalPath` | `uploads` | Folder for uploaded files (relative to the content root) |
| `ImageStorage:Gcs:Bucket` | *(empty)* | Bucket name (required for `GoogleCloudStorage`); credentials only via `GOOGLE_APPLICATION_CREDENTIALS` |
| `ImageStorage:Gcs:ObjectPrefix` | `listings` | Folder inside the bucket |
| `ImageStorage:Gcs:PublicBaseUrl` | *(empty)* | Optional CDN / custom domain for the public URLs |
| `Admin:Usernames` | `[]` | Users who may approve requested makes/models |
| `ImageUpload:MaxFiles` | `20` | Max images per item |
| `ImageUpload:MaxFileSizeMB` | `5` | Max size of one image |
| `ImageUpload:AllowedExtensions` | `.jpg .jpeg .png .webp` | Accepted image types |
| `Geocoding:Provider` | `BuiltIn` | Reverse geocoder: `BuiltIn` (offline city list for India, USA, Nigeria, Ireland; nearest city within 150 km) or `Google` (Google Geocoding API) |
| `Geocoding:CacheHours` | `24` | Answers are cached per point rounded to 2 decimals (~1 km) |
| `Geocoding:Google:ApiKey` | *(empty)* | Only from an environment variable (`Geocoding__Google__ApiKey`) or user-secrets – never in appsettings |

## Listings (sell flow)

Migration `ListingModel` (additive) adds master data – `Categories`, `PartTypes` (synonyms, `SpecFieldsJson`,
`HasPosition`/`HasSide`/`IsBodyPart`, default `FitmentMode`), `Makes`, `VehicleModels` (aliases, `Pending` until approved),
`VehicleConfigs` – seeded by `Data/CatalogSeed`, and per listing `PartDetails` (brand type, used grade, side/position),
`PartFitments` (make, model, year from/to, config), `PartNumbers` (as typed + normalised), `PartSpecs` (key/value, e.g. tyre size),
`VehicleDetails` (VIN – shown masked to buyers, mileage, damage areas, `PapersJson` per country), `Orders` and `GarageVehicles`.
`Item` gains `Kind` (`CommonPart` / `VehicleSpecificPart` / `Vehicle`), `FitmentMode` (`Universal` / `SizeBased` /
`VehicleSpecific`), `PartTypeId`, `Title`/`TitleExtra`, `CountryCode`/`Currency` (from the location, `RegionRules`),
`DeliveryOptions` (flags), `SizeClass`, `DurationDays`; `Auction` gains `BidCount` and the seller's contact (from the token,
shown to the buyer only after the sale); `Status` gains `Draft` (4) and `Cancelled` (5). Drafts and cancelled listings
are never returned by `GET api/auctions` and are not published to other services.

`Services/ListingRules`: builds the title ("2016–2020 Toyota Hilux Front Bumper – Genuine OEM – Used (Grade A)") and the
fitment summary, keeps the legacy `Make`/`Model`/`Year`/`PartName` fields in sync, and validates before publishing:
part type; ≥ 1 fitment for vehicle-specific parts; required spec fields for size-based parts; brand type, condition,
used grade, position/side when the part type has them; vehicle identity, condition and the country's papers;
3–10 photos for parts and 8–20 for vehicles; price > 0; location and a delivery option; duration 1, 3, 5, 7 or 10 days;
no phone numbers / e-mails in the title, description or notes.
After the first bid only photos and description notes can be added.

**Orders**: `AuctionFinished` with `Sold` creates an `Order`. Tracking (`Services/OrderTracking`, migration
`OrderTracking`): the steps depend on the delivery method – seller ships: Part won → Packed → Dispatched → Out for
delivery → Delivered; pickup: Part won → Ready for pickup → Picked up; buyer's courier: Part won → Packed → Handed to
courier → Delivered. The method is set at once when the listing offered one option, otherwise the buyer chooses it.
Each step is stored in `OrderEvents` (time, user, courier note) and publishes **OrderStatusChanged** (exchange
`order-status-changed`) so NotificationService tells the other party.
`BidPlaced` updates `BidCount`. Demo data: `Data/DemoListings` adds 17 listings in India, Nigeria, the USA and Ireland with generated
placeholder photos, and converts older auctions to the new model once.

## Custom orders

`PartRequests` table (entity `PartRequest`): a buyer's custom order created when a search finds nothing – search text,
part type (`PartName`), part number (as typed + normalised: uppercase, letters/digits only), optional make/model/year
(empty/0 = any), the buyer's location and radius, and status **Open / Matched / Closed**.

When a listing is published (create or relist), `CustomOrderMatcher` checks every **Open** order of other users in the
same transaction. A part listing matches when it is within the buyer's radius (default 60 km; listings without a
location never match a buyer who has one) AND either the normalised part numbers are equal, or every word of the
ordered part type appears in the listing's part name/description and the vehicle fits (same make, model, and the year
inside `Year..YearTo`). Matching orders become **Matched** (`MatchedAuctionId`, `MatchedAt`) and **CustomOrderMatched**
is published through the outbox; NotificationService sends it to that buyer only. The app also announces matched
orders it hasn't shown yet when it opens.

Bid step: the per-auction `BidIncrement` is optional now (defaults to 1) – BidService uses its tier table instead.

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

### Chat-related events

- Publishes **OrderCreated** `{ orderId, auctionId, seller, buyer }` when an order is created (auction won or direct sale bought) – ChatService lets the seller message that buyer.
- Consumes **UserBlocked / UserUnblocked** (ChatService): a blocked user can't buy the blocker's direct sales (`POST api/listings/:id/buy` → 400).

## Ratings & reviews

Verified purchases only: the **buyer of a Completed order** reviews it **once**, up to **30 days** after completion, and
can edit it within **7 days** of posting. Sellers can't review themselves. Every change recalculates the seller's
summary (`SellerRatings`: average, count, 5★…1★, quick-rating averages), copies average + count onto the seller's
listings (`AuctionDto.sellerRating` / `sellerRatingCount`) and publishes **SellerRatingChanged** (SearchService,
ChatService). When an order is completed a durable **ReviewReminder** is scheduled for 2 days later; if there is still
no review it publishes **ReviewRequested** (NotificationService in-app, ChatService push).

| Verb | Path | Description | Auth |
|---|---|---|---|
| POST | api/reviews | `{ orderId, itemRating 1–5, sellerRating 1–5, communication?, accuracy?, delivery? (1–5), tags[], comment? (≤ 1000, no contact details), photoIds[] (≤ 5, the buyer's own /media?purpose=review uploads) }` → 201 **ReviewDto**. 403 not the buyer; 400 not completed / already reviewed / after 30 days / validation (field errors) | Auth (buyer) |
| GET | api/reviews/:id | One review (hidden ones only for the author and admins) | Anon |
| PUT | api/reviews/:id | Edit (same body) within 7 days of posting | Auth (author) |
| POST | api/reviews/:id/reply | `{ text }` – the seller's one public reply | Auth (seller) |
| POST | api/reviews/:id/report | `{ reason: spam\|abuse\|fake\|personal\|other, details? }` | Auth |
| GET | api/reviews/listing/:listingId | Item reviews of a listing, newest first | Anon |
| GET | api/sellers/:username | **SellerRatingDto**: average, count, isNewSeller (< 3), breakdown [5★…1★], communication / accuracy / delivery / item averages, completed sales, 5 latest reviews | Anon |
| GET | api/sellers/:username/reviews?page= | 20 per page, newest first | Anon |
| GET | api/reviews/admin/reports; POST api/reviews/:id/hide, …/show | Open reports / hide a review (not shown, not counted; its reports closed) / show it again | Admin (`Admin:Usernames`) |

Reviews show the buyer partly hidden (`a***e`) to everyone but the buyer. `OrderDto` has `reviewId`, `canReview` and
`reviewDeadline`.


## Payments (Stripe, test mode) – Step 1

Design: `bmwmobile/docs/PAYMENTS_DESIGN.md`. Package `Stripe.net` 53. Code: `Payments/` (`IPaymentProvider`, `StripePaymentProvider`,
`NotConfiguredPaymentProvider`, `SellerPaymentService`, `PaymentWebhookService`, `PaymentAmounts`, entities), `Controllers/PaymentsController.cs`.
Without `Stripe:SecretKey` (user-secrets) payments are off and nothing changes for sellers or buyers.

| Method | Path (Gateway) | Who | Notes |
|---|---|---|---|
| GET | `/payments/config` | signed in | `{ enabled, publishableKey, countries, commissionPercent }` |
| GET | `/payments/seller-account` | seller | status: `NotAvailable` / `NotStarted` / `Pending` / `Active` / `ActionNeeded`, `canPublish` |
| POST | `/payments/seller-account` | seller | first visit: opens the Stripe Express account (US / IE) |
| POST | `/payments/seller-account/onboarding-link` | seller | `{ url }` – Stripe-hosted onboarding |
| POST | `/payments/seller-account/dashboard-link` | seller | `{ url }` – Stripe Express dashboard (after onboarding was submitted) |
| GET | `/payments/stripe/onboarding-return`, `/payments/stripe/onboarding-refresh` | anyone | pages that send the browser back to `shopcarparts://payments` |
| POST | `/payments/stripe/webhook` | Stripe | no sign-in; signature checked; idempotent; only `app=shopcarparts` objects |
| POST | `/api/internal/payment-accounts` (not via Gateway) | IdentityService | `{ seller, country, email }` when an admin creates / upgrades a seller |

Rules: publishing / relisting a listing in the USA or Ireland needs the seller's payment account **Active** (409 `PAYMENTS_NOT_READY`).
Seller accounts are created with Stripe **Accounts v2** (`POST /v2/core/accounts`: Express dashboard, fees / losses =
application, merchant `card_payments` + recipient `stripe_transfers`, MCC 5533, `metadata.app`); status, onboarding links,
the Express dashboard link and transfers use the v1 APIs, which work with v2 accounts. `account.updated` (v1) keeps the
status in sync.
Settings `Payments:*` (commission %, deadline hours, second-chance offers, reservation minutes, auto-complete days, strikes); keys `Stripe:*`.
Tables: `SellerPaymentAccounts`, `BuyerPaymentCustomers`, `Payments`, `PaymentTransfers`, `PaymentRefunds`, `PaymentWebhookEvents`,
`StockReservations`, `SecondChanceOffers`, `NonPaymentStrikes` (migration `Payments`).
## Payments – Step 2: buyers pay (Buy now, auction payment deadline, second-chance offers, strikes)

Code: `Payments/CheckoutService.cs` (Buy now reservation, order payment, webhook outcomes), `Payments/PaymentDeadlines.cs`
(`PaymentDeadlineService`, `StrikeService`, `PaymentJobs` background job, `IBidHistory`), `Services/DeliveryChoice.cs`.
Separate charges and transfers: the PaymentIntent is created on the platform (`transfer_group=order_<orderId>`,
`metadata.app`, `metadata.payment_id`, idempotency key `pi-<paymentId>`); the amount is always calculated by the backend
(item price or winning bid × quantity + shipping, in cents). An order becomes `Paid` **only** from the verified
`payment_intent.succeeded` webhook whose amount and currency match.

| Method | Path (Gateway) | Who | Notes |
|---|---|---|---|
| POST | `/payments/checkout/buy-now` | buyer | `{ listingId, quantity, delivery: { method: Pickup \| Shipping, addressId?, address?, saveAddress } }` → **CheckoutSessionDto** `{ paymentId, clientSecret, customerId, ephemeralKey, publishableKey, currency, itemAmount, shippingAmount, totalAmount, reservedUntil, orderId }`. Reserves the units for `ReservationMinutes` (`QuantityReserved`); the order is created by the webhook |
| POST | `/payments/orders/:orderId/pay` | buyer (winner) | auction / second-chance order `AwaitingPayment`; delivery chosen first (400 `DELIVERY_REQUIRED`) → CheckoutSessionDto. Reuses the open PaymentIntent |
| POST | `/payments/:paymentId/cancel` | buyer | sheet closed / screen left: cancels the PaymentIntent, releases the reservation → 204 |
| GET | `/payments/:paymentId` | buyer | `{ paymentId, status: Pending \| Succeeded \| Failed \| Canceled \| Refunded, orderId, failureMessage, reservedUntil }` (the app polls it after the sheet) |
| GET | `/payments/offers` | bidder | open second-chance offers `{ id, listingId, title, amount, currency, expiresAt, round }` |
| POST | `/payments/offers/:id/accept`, `/payments/offers/:id/decline` | bidder | accept → the order moves to this bidder (`AwaitingPayment`, due = the offer's end) → **OrderDto**; decline → next bidder |
| GET | `/payments/strikes/me` | signed in | `{ strikes, limit, blockedReason }` |
| POST | `/payments/admin/strikes/:username/clear` | admin | clears the user's active strikes → `{ cleared }` |
| POST | `/api/listings/:id/buy` | – | USA / Ireland with payments on: 409 `PAYMENT_REQUIRED` (use checkout); India / Nigeria unchanged |

Errors `{ message, code }`: `RESERVED` / `QUANTITY` (409, another buyer holds or took the units), `SELLER_NOT_READY` (409),
`DEADLINE_PASSED` / `OFFER_ENDED` (409), `NOT_BUYER` (403), `BLOCKED`, `OWN_LISTING`, `NOT_FIXED_PRICE`,
`SHIPPING_NOT_AVAILABLE`, `ADDRESS_INVALID`, `PAYMENTS_NOT_AVAILABLE`, `NOT_FOUND` (404).

Order states: `AwaitingPayment` (8) and `Cancelled` (9); `OrderDto` adds `paymentStatus` (`NotRequired` / `AwaitingPayment` /
`Paid` / `Refunded`), `paymentDueAt`, `paidAt`, `canPay`, `paymentLabel`, `cancelReason`. My Listings states
`AwaitingPayment`, `OfferedToNextBidder`, `UnsoldNotPaid` (relist allowed).

Background job `PaymentJobs` (every `JobIntervalSeconds`): releases expired reservations (cancels the PaymentIntent),
sends the halfway and the last reminder (2 h before, or the last quarter of a shortened deadline), cancels unpaid orders
("Not paid in time") with a **strike** for the auction winner, offers the item to the next-highest bidder at their own highest
bid (bids from BidService `GET /api/bids/:auctionId`; skips the seller, users already asked, users blocked from bidding and
users the seller blocked), and expires offers. After `SecondChanceOffers` rounds the listing is `UnsoldNotPaid`.
A payment that succeeds after its reservation / order closed is refunded (`refund-<pi>` idempotency key).

gRPC **GetBidderStanding(bidder) → { blockedReason }**: BidService refuses bids while the user has `StrikeLimit` strikes in
`StrikeWindowMonths` (empty = may bid; errors fail open).

Settings added: `Payments:PaymentDeadlineMinutes` (null; demo override of the 48 h), `JobIntervalSeconds` (30), `RunJobs`
(true), `BidServiceUrl` (`http://localhost:7003`), **`AllowInactiveSellersInTestMode` (false)** – test only: sellers may publish
and be paid without an Active payment account; ignored unless the key is a test key. Migration `PaymentsBuyers`:
reservation delivery fields, `Payments.OrderId` nullable + `ReservationId` / `AuctionId`, order payment columns,
`Auctions.QuantityReserved` (concurrency token).


## Payments – Step 3: payouts, auto-complete, refunds, disputes, receipts, admin

Code: `Payments/SellerPayouts.cs` (`SellerPayoutService`), `OrdersController` (handover → `AutoCompleteAt`; Completed → payout),
`PaymentJobs` (auto-complete + payout retries), `PaymentWebhookService` (refunds / disputes / transfers). Migration `PaymentsPayouts`.

| Method | Path (Gateway) | Who | Notes |
|---|---|---|---|
| GET | `/payments/orders/:orderId/receipt` | buyer / seller | **ReceiptDto**: buyer – item, shipping, total, currency, paidAt, card ("visa •••• 4242"), `receiptUrl` (Stripe), refunds; seller – commission %, commission, `payout` |
| POST | `/payments/orders/:orderId/problem` | buyer | `{ text }` – paid online, not completed: order on hold (no auto-complete, no payout), seller notified → **OrderDto** |
| POST | `/payments/orders/:orderId/cancel-and-refund` | seller | `{ text }` – paid online, before Picked up / Shipped: full refund, order `Cancelled` ("Refunded – Cancelled by the seller: …"); 409 after the handover or when already refunded |
| GET | `/payments/admin/payments?status=&user=&take=50` | admin | payments with their order, hold, auto-complete and payout (**AdminPaymentDto**) |
| GET | `/payments/admin/orders/:orderId` | admin | one order's payment, refunds, payout, dispute, with the Stripe ids (`pi_`, `ch_`, `tr_`, `dp_`) and the exchange rate |
| POST | `/payments/admin/orders/:orderId/refund` | admin | `{ amount? (cents), reason }` – all that is left (no amount) or part of it |
| POST | `/payments/admin/orders/:orderId/hold` / `release` | admin | hold `{ text }`; release `{ complete }` – complete = also complete the order now and pay the seller |
| POST | `/payments/admin/transfers/:transferId/retry` | admin | send a Pending / Frozen payout again now (409 otherwise) |
| POST | `/payments/admin/transfers/:transferId/reverse` | admin | `{ amount? (cents of the transfer's currency), reason }` – take a sent payout (partly) back |

Rules:
- **Payout** when the order becomes Completed (buyer, job, support): transfer = item + shipping − commission − refunds,
  `source_transaction` = the buyer's charge, `transfer_group=order_<id>`, idempotency `tr-<transferId>`. EUR orders: sent in
  `Payments:PlatformCurrency` (USD) at the charge's balance exchange rate (never more than the charge brought in); what arrived in
  the seller's account (EUR) is read back from their balance transaction. Not Active / Stripe error → Pending, retried by the
  job (1, 4, 9 … min, at most hourly). On hold / disputed → Frozen.
- **Auto-complete** `Payments:AutoCompleteDays` (7) after Picked up / Shipped (`AutoCompleteAt`), paid online, not on hold;
  event by `system`, "Completed automatically 7 days after …".
- **Refunds** to the card, idempotency `refund-<refundRowId>`; `charge.refunded` records Dashboard refunds ("Refunded in the
  Stripe Dashboard", by `stripe`). After a payout the seller's part of the refund is reversed (`rev-<refundRowId>`). A full refund
  sets the order's payment `Refunded` and cancels an order that wasn't completed.
- **Disputes** (`charge.dispute.*`, matched by charge – disputes have no metadata): created / updated → payment `Disputed`, order
  on hold, payout Frozen; closed won → released (paid if completed); lost → payout Cancelled (already sent: admin reverse).

`OrderDto` adds `receiptUrl` (buyer), `refundedCents`, `payout` `{ amountCents, currency, approximate, status, label, transferredAt }`
(seller; status AfterCompletion / Scheduled / Sending / WaitingForSetup / OnHold / Transferred / Reversed / Cancelled; label
e.g. "You'll receive about €94.05 when the buyer confirms, or automatically on 23 Oct", "Paid out €94.04 (sent as $105.44)"),
`autoCompleteAt`, `onHold`, `holdReason`, `canReportProblem`, `canCancelAndRefund`. Payment statuses add `PartiallyRefunded`,
`Disputed`; transfer statuses add `Cancelled`.

Settings: `Payments:AutoCompleteMinutes` (null; demo override of the 7 days), `Payments:PlatformCurrency` (`USD`).
