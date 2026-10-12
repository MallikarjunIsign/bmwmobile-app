# SearchService

## Summary

Read-only, denormalized search index for auctions (vehicles and parts), backed by Meilisearch rather than a
relational DB. Builds its index purely from RabbitMQ events (create/update/delete/finish/bid/extend/container),
and on startup catches up on anything it missed by asking AuctionService for auctions updated since its most
recently indexed document. Exposes anonymous search/filter/sort endpoints — no write API, no auth.
**The reserve price is never indexed**; documents only carry `reserveMet`.

## Infrastructure

- .Net Web API (Minimal APIs)
- Meilisearch (search index — no relational DB)
- Service Bus - RabbitMQ (WolverineFx)
- HttpClient with HTTP resilience handler (startup catch-up sync against AuctionService)

## Nuget Packages

* Mapster
* MeiliSearch
* Microsoft.Extensions.Http.Resilience
* WolverineFx.RabbitMQ
* WolverineFx.RuntimeCompilation

## External (User)

None — SearchService has no authenticated write endpoints; it only builds its index from events.

## Queries handled

- **Search** - Full-text search (`make`/`model`/`partName`/`partNumber`/`description`/container number) with filters and sorting, paginated (`pageNumber`, `pageSize` capped at 50). Returns `{ results, pageCount, totalCount }`
- **GetById** - Gets a single indexed item by ID. Returns 404 if not found

### Search parameters

| Param | Values | Filter |
|---|---|---|
| `searchTerm` | text | full-text |
| `seller`, `winner` | username | exact match |
| `filterBy` | `live` | `status = Live` and `auctionEnd` in the future |
| | `endingSoon` | live and ending within 6 hours |
| | `finished` | `status` is `Sold`, `ReserveNotMet` or `Unsold`, or `auctionEnd` has passed |
| `itemType` | `all` (default) / `vehicle` / `part` (plural and any case accepted) | `itemType` |
| `partCategory` | `Body`, `Engine`, … `WheelsAndTyres`, … | `partCategory` (Marketplace category pill) |
| `make`, `model` | exact names | `make`, `model` (Marketplace make/model pill; combine with each other and `searchTerm`) |
| `country` | `IN` / `US` / `NG` / `IE` | only without `lat`/`lon`: listings inside the country's boxes (`Contracts.CountryBounds`) – the app's "country-wide" results while the user's location is off |
| `condition` | `New` / `Used` / `Refurbished` | `condition` |
| `destination` | port or country | `container.destinationPort` or `container.destinationCountry` |
| `orderBy` | `make` / `new` / `endingSoon` / `distance` | sort (`distance` = nearest first, only with `lat`/`lon`) |
| `lat`, `lon`, `radiusKm` | coordinates; radius in km (default 50, max 500) | items within the radius (see below) |

All filter values are quoted and escaped before being put into the Meilisearch filter expression.

### Parts within X km

With `lat` and `lon` (both required and valid, otherwise `400`) the search only returns items whose `latitude`/`longitude` are within `radiusKm`:
1. Meilisearch pre-filters with a bounding box (`Contracts.GeoMath.BoundingBox`) on the filterable `latitude`/`longitude` attributes, together with every other filter and the text search (up to 1000 candidates).
2. The exact Haversine distance (`Contracts.GeoMath.HaversineKm`) removes the box's corners; each result gets `distanceKm` (1 decimal).
3. Paging is applied after the distance check, so `totalCount`/`pageCount` are exact.

Items without a location (older listings) never match a radius search. Without `lat`/`lon` the search works exactly as before (no `distanceKm`).

## Events emitted

None.

## Events consumed

- **AuctionService.AuctionCreated** - Adds the auction as a document to the `auctions` index (the reserve is dropped because `Item` has no such field) and waits until it is indexed. Includes a deliberate test hook: `Make == "fail-once"` throws once per auction ID, `Make == "fail-always"` always throws — both as `TransientSearchException`, which Wolverine retries (2x, 500ms/500ms/1s cooldown) before routing to the error queue, demonstrating the retry/dead-letter policy
- **AuctionService.AuctionUpdated** - Replaces the item, pricing, image and container details from the snapshot; keeps the bid/end fields the index already has (`currentHighBid`, `reserveMet`, `status`, `endReason`, `sellerDecisionDeadline`, `winner`, `soldAmount`, and an `auctionEnd` extended by anti-sniping)
- **AuctionService.AuctionDeleted** - Removes the document from the index
- **AuctionService.ContainerUpdated** - Partially updates the `container` field of every auction in `AuctionIds`
- **BidService.AuctionFinished** - Applies the end status idempotently: `Live` → any end status, `ReserveNotMet` → `Sold`/`Unsold`; anything else (duplicates, late messages) is ignored. Sets `status`, `endReason`, `sellerDecisionDeadline` (while ReserveNotMet), and `winner`/`soldAmount` when Sold
- **BidService.BidPlaced** - For an `Accepted`/`AcceptedBelowReserve` bid higher than the current one: sets `currentHighBid` and `reserveMet` (= status `Accepted`); lower, out-of-order bids are ignored
- **BidService.AuctionExtended** - Moves `auctionEnd` later

Events arrive on separate queues, so e.g. a bid can be handled before its auction has been indexed. A Meilisearch `document_not_found` error is therefore retried (1s, 2s, 5s, 10s) before the message goes to the error queue.

## Startup catch-up sync

On boot, `DbInitializer` deletes the legacy `items` index (its documents contained the reserve price, and partial updates can't remove a field), configures the `auctions` index settings (searchable/filterable/sortable attributes), then calls `AuctionSvcHttpClient.GetItemsForSearch()`, which looks up the most recently `updatedAt` document already indexed and calls `AuctionService.GET /api/auctions?date=<that timestamp>` to fetch anything newer, and bulk-adds it to the index. This closes the gap for auctions created/updated while SearchService was down (events published to a queue it wasn't yet bound to, or a fresh index). AuctionService bumps `UpdatedAt` on end-status changes and container updates so these are caught up too.

## API Endpoints

|GET|api/search|Search auctions (see *Search parameters*)|Anon|
|---|---|---|---|
|GET|api/search/filters|Marketplace pills: `{ categories: [{value,count}], makes: [{value,count}], models: [{make,model,count}], totalCount }` for **live** listings within `lat`/`lon`/`radiusKm` (or `country`), most listings first, `top` (default 10) each|Anon|
|GET|api/search/listings|E-commerce search (see *Listing search*) → `{ results, totalCount, pageCount, chips, keywords, facets, priceMin, priceMax }`|Anon|
|GET|api/search/suggest?q=&country=|Suggestions while typing → `[{ group: parts \| vehicles \| partNumbers, label, chips, subtitle? }]`|Anon|
|GET|api/search/:id|Get an indexed auction by id|Anon|

## Listing search

`GET api/search/listings` parameters (all optional): `q`, `partType`, `make`, `model`, `year`, `category`, `kind`
(`CommonPart` / `VehicleSpecificPart` / `Vehicle`), `condition`, `brandType`, `minPrice`, `maxPrice`, `lat`/`lon`/`radiusKm`
(default 60 km), `maxDistanceKm`, `country` (when there is no location), `delivery` (`Pickup` / `Shipping`), `seller` (one seller's listings – without
location / country they are returned wherever they are), `saleType` (`Auction` / `FixedPrice`), `sort` (`best` default,
`endingSoon`, `newest`, `priceAsc`, `priceDesc`, `nearest`), `page`, `pageSize`. Only Live listings that haven't ended are
returned. Hits carry `saleType`, `sellerName` and `quantityAvailable`; for FixedPrice listings **AuctionUpdated** sets the
status too (Sold when sold out), since BidService never sends events for them.

**Query understanding** (`Services/QueryParser` with `SearchDictionary`, loaded from AuctionService
`GET api/catalog/dictionary` and refreshed by `DictionaryRefresher`): tyre sizes (`265/65R17` → `specKeys`
`tyresize:265/65r17`), years and year ranges, part numbers (with or without dashes/spaces → `partNumbersNormalized`),
part types and their synonyms, positions (front/rear/left/right), makes, models and aliases (n-grams); what is left
becomes keywords. The understood parts come back as `chips`.

**Fitment**: each document has `fitKeys` (`make`, `make|model`, `make|model|year` for every year of every fitment).
A vehicle in the query matches vehicle-specific listings with that key, plus universal / size-based listings of the
same part type. The search is never relaxed silently – no matches means an empty result (the app then offers a custom order).

**Ranking** (`matchTier`): 0 part number → 1 exact vehicle + part type → 2 make + model → 3 universal / size match →
4 keywords; ties nearest first (or by the chosen `sort`). **Badge**: "Fits your 2016 Toyota Hilux", "Universal",
"Fits by size" or "Check fitment". **Facets**: counts per category, part type, make, condition and brand type.

The `Item` document gains the listing fields (`kind`, `fitmentMode`, `partType`, `title`, `fitmentSummary`, `fitments`,
`partNumbers`, `specs`, `brandType`, `usedGrade`, `bidCount`, `thumbnailUrl`, `currency`, …) and the derived
`fitKeys`, `partNumbersNormalized`, `specKeys` and `currentPrice` (`Item.Prepare()`). On startup the index settings
are updated and documents missing the derived fields are re-indexed.

## Models

**Item.cs** (Meilisearch document in index `auctions` — mirrors AuctionService's public `AuctionDto`, **no ReservePrice**)

| Property Name   | Property Type | Default Value |
| ---------------- | -------------- | -------------- |
| Id                | string         |                |
| StartingPrice     | int            |                |
| BidIncrement      | int            |                |
| ReserveMet        | bool           |                |
| Seller            | string         |                |
| Winner?           | string         |                |
| SoldAmount?       | int            |                |
| CurrentHighBid?   | int            |                |
| CreatedAt         | DateTime       |                |
| UpdatedAt         | DateTime       |                |
| AuctionEnd        | DateTime       |                |
| Status            | string (Live / Sold / ReserveNotMet / Unsold) | |
| EndReason?        | string         |                |
| SellerDecisionDeadline? | DateTime |                |
| ItemType          | string (Vehicle / Part) | Vehicle |
| Make, Model       | string         |                |
| Year, YearTo?     | int            |                |
| Color?, Mileage?  | string, int    |                |
| Description       | string         |                |
| PartName?, PartNumber?, PartCategory?, Condition? | string | |
| Quantity?         | int            |                |
| Container?        | ContainerInfo (`id`, `containerNumber`, `originPort`, `originCountry`, `destinationPort`, `destinationCountry`, `departureDate`, `estimatedArrivalDate`, `shippingStatus`) | |
| ImageUrl          | string (primary image) |        |
| Images            | string[]       |                |

Index config: `SearchableAttributes = [make, model, partName, partNumber, description, container.containerNumber]`, `FilterableAttributes = [seller, winner, status, auctionEnd, itemType, partCategory, condition, container.id, container.destinationCountry, container.destinationPort]`, `SortableAttributes = [auctionEnd, currentHighBid, createdAt, updatedAt, make, model]`.

## DTOs

None — the Meilisearch `Item` document is returned directly (as search hits, or the raw document for get-by-id).

## Event Consumed Types

**AuctionService.AuctionCreated** / **AuctionService.AuctionUpdated** - the full `AuctionSnapshot` (see auction-service.md); `ReservePrice` is ignored.

**AuctionService.AuctionDeleted** - `Id`

**AuctionService.ContainerUpdated** - `ContainerInfo` fields + `AuctionIds`

**BidService.AuctionFinished** - `ItemSold`, `AuctionId`, `Winner?`, `Seller`, `Amount?`, `Status?`, `EndReason?`, `SellerDecisionDeadline?`, `BidId?`

**BidService.AuctionExtended** - `AuctionId`, `AuctionEnd`

**BidService.BidPlaced**

|Property Name|Property Type|Default Value|
|---|---|---|
|Id|string||
|AuctionId|string||
|Bidder|string||
|BidTime|DateTime||
|Amount|int||
|BidStatus|string||

### Seller ratings

Documents carry `sellerRating` / `sellerRatingCount` (from the snapshot, and updated for all of a seller's listings by
**SellerRatingChanged**). `GET api/search/listings` also takes `minSellerRating` ("Seller rating 4★ & up") and
`sort=topRated` ("Top-rated sellers": rating, then number of ratings, then best match).
