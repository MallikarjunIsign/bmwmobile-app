---
title: "ShopCarParts – Part, Vehicle and Auction Listing & Search Design"
subtitle: "Fields to capture, listing flows and how search finds the right part · 8 October 2026"
---

# 1. Purpose

When a buyer types something like **"2016 hilux front bumper"**, **"bonnet for swift"** or a part number, the app must show the parts that actually fit. That only works if sellers give us the right details, in the same structured way, when they list a part or a vehicle.

This document defines:

- **What to capture** when adding a part, a universal part, a whole vehicle and an auction.
- **The flows** for adding a part and adding a vehicle.
- **How search uses those details** to filter and rank results.
- **The data model**, the master data needed, and a phased plan.

The design follows how established parts marketplaces and the automotive aftermarket structure data: fitment by **Year, Make, Model, Trim and Engine** (the eBay Motors compatibility model and the ACES fitment standard), with product details such as brand, part number and specifications kept separately (the PIES standard).

# 2. Key ideas (in plain words)

**1. Three kinds of parts – the "fitment mode".** Not every part needs vehicle details. Each part type has one of three modes:

| Fitment mode | Meaning | Examples | What the seller enters |
|---|---|---|---|
| **Vehicle-specific** | Fits only certain vehicles | Front bumper, headlamp, ECU, gearbox, door, alloy wheel | Which vehicles it fits (year, make, model …) |
| **Spec-based** | Fits any vehicle that takes that size / spec | Tyre 265/65 R17, bulb H4, battery, wiper blade 24", engine oil 5W-30 | The size / spec only |
| **Universal** | Fits any vehicle | Dash cam, phone holder, tool kit, car cover, jump starter | No vehicle details |

The mode is set on the **part type** (so the seller doesn't have to know it), but the seller can change it – for example a "universal-fit" seat cover vs. a custom-fit one.

**2. Fitment is a list.** One part often fits many vehicles (a mirror that fits Toyota Hilux 2016–2020 *and* Toyota Fortuner 2016–2020). Sellers add one or more fitment rows, each with a **year range**.

**3. Required minimum, optional detail.** For a vehicle-specific part, **Year (range) + Make + Model** are required. Trim, engine, fuel, transmission, drivetrain and steering side are optional ("Any"), and only shown when they matter for that part type (e.g. transmission matters for a gearbox, not for a bumper).

**4. Pick from lists, don't type.** Make, model, trim, part type and condition are chosen from lists (master data), so "Toyota", "toyota" and "TOYOTA" never become three different makes. Free text is only used for description and notes.

**5. Local words.** India, Nigeria and the USA use different words for the same part (bonnet / hood, boot / dickey / trunk, silencer / muffler, tyre / tire, windscreen / windshield). Search treats them as the same.

**6. Left / right, not driver / passenger.** India drives right-hand-drive vehicles, while the USA and Nigeria drive left-hand-drive. "Driver side" means opposite sides in each, so position is stored as **Left / Right as seen from the driver's seat**, plus an optional **steering side (LHD / RHD)** on fitment for parts that differ (dashboard, steering rack, headlamp beam pattern, wiper arms).

# 3. Part catalogue (categories and part types)

Three levels: **Category > Sub-category > Part type**. A starter list (to be extended by admins):

| Category | Example part types | Usual fitment mode |
|---|---|---|
| Engine & Mechanical | Engine assembly, cylinder head, turbocharger, alternator, starter motor, timing kit | Vehicle-specific |
| Transmission & Drivetrain | Gearbox, clutch kit, CV axle / driveshaft, differential, transfer case | Vehicle-specific |
| Suspension & Steering | Shock absorber, strut, control arm, steering rack, wheel hub / bearing | Vehicle-specific |
| Brakes | Brake pads, discs / rotors, calipers, ABS module | Vehicle-specific |
| Electrical & Electronics | ECU, wiring harness, sensors, infotainment unit | Vehicle-specific |
| | Battery | Spec-based |
| Lighting | Headlamp, tail lamp, fog lamp, indicator | Vehicle-specific |
| | Bulbs (H4, H7 …), LED kits | Spec-based |
| Body – Exterior | Front / rear bumper, bonnet (hood), fender (wing), door, boot lid / tailgate, grille, side mirror | Vehicle-specific |
| Glass | Windscreen, door glass, rear glass | Vehicle-specific |
| Interior | Seats, dashboard, steering wheel, door trims | Vehicle-specific |
| | Seat covers, floor mats | Universal or vehicle-specific |
| Wheels & Tyres | Alloy wheels | Vehicle-specific + spec (bolt pattern, size) |
| | Tyres | Spec-based |
| Cooling & AC | Radiator, AC compressor, condenser, fan | Vehicle-specific |
| Fuel & Exhaust | Fuel pump, injectors, silencer (muffler), catalytic converter | Vehicle-specific |
| Filters & Fluids | Oil / air / fuel filters | Vehicle-specific |
| | Engine oil, coolant, wiper blades | Spec-based |
| Accessories & Tools | Dash cam, phone holder, car cover, tool kit, jump starter, car care | Universal |

Each **part type** also stores: its synonyms (for search), whether it has a **position** (front / rear, left / right), and which **spec fields** it needs (for example *Tyre: width, profile, rim size, load index, speed rating*; *Bulb: fitting type, wattage*; *Battery: capacity Ah, terminal layout, size group*).

# 4. Adding a part

(See the flow diagram in the Word version of this document.)

## 4.1 Step 1 – Part type

| Field | Required | How it's entered | Example |
|---|---|---|---|
| Part type | Yes | Search-as-you-type over part types and synonyms, or browse Category > Sub-category > Part type | "bonnet" → Body – Exterior > Bonnet (Hood) |
| Fitment mode | Auto | Filled from the part type; seller can switch | Vehicle-specific |

## 4.2 Step 2 – Fitment (vehicle-specific parts only)

The seller adds one or more rows. Each row:

| Field | Required | Notes |
|---|---|---|
| Year from – Year to | Yes | One year or a range (max 30 years) |
| Make | Yes | From list (Toyota, Maruti Suzuki, Honda, Ford …) |
| Model | Yes | From list for that make (Hilux, Swift, Civic, F-150 …) |
| Trim / Variant | Optional | e.g. SR5, VXi, EX – "Any" by default |
| Engine | Optional | Size and type, e.g. 2.8 L diesel |
| Fuel | Optional | Petrol, Diesel, CNG, Hybrid, Electric |
| Transmission | Optional* | Manual / Automatic / CVT / DCT |
| Drivetrain | Optional* | 2WD-FWD, 2WD-RWD, 4WD, AWD |
| Steering side | Optional* | LHD / RHD |
| Market / region | Optional | e.g. Indian-market, US-market (same model name can differ by country) |
| Fitment note | Optional | "Only for models with fog lamps" |

\* Shown as important for the part types where it matters (gearbox, clutch, driveshaft → transmission/drivetrain; dashboard, steering rack, headlamp → steering side).

**Shortcuts:**

- **VIN** – if the seller has the donor vehicle's VIN, decode it to fill year, make, model, engine and drivetrain. The free NHTSA vPIC service decodes VINs for vehicles sold in the US market (many foreign-used vehicles in Nigeria are US-spec); Indian-market vehicles are picked from our lists.
- **OEM part number** – if we already have a listing with the same OEM number, offer its fitment list to copy.
- **"Copy fitment from my last listing"** for sellers listing many parts from one vehicle.

## 4.3 Step 2 (alternative) – Specs (spec-based parts)

Only the spec fields for that part type, e.g. *Tyre: 265 / 65 / R17, load 112, speed T, tread depth 6 mm, year of manufacture 2024*. No vehicle details.

**Universal parts skip step 2 completely.**

## 4.4 Step 3 – Part details

| Field | Required | Values / example | Why |
|---|---|---|---|
| Brand type | Yes | Genuine OEM, OEM-equivalent, Aftermarket | Buyers filter on it |
| Brand | If not OEM | Bosch, Denso, TYC … | Search + trust |
| OEM part number(s) | Recommended | 52119-0K935 | Exact search by part number |
| Aftermarket / interchange numbers | Optional | Brand's own number, equivalent numbers | Same |
| Position | If part type has one | Front / Rear; Left / Right / Centre | Bumper, lamp, door, mirror |
| Condition | Yes | New, Used, Refurbished / Remanufactured, For repair / not working | Main buyer filter |
| Used grade | If Used | A – very little or no damage; B – minor damage; C – noticeable damage | Common grading idea in used-parts trade; based on visible damage |
| Mileage of donor vehicle | Optional (used mechanical) | 85,000 km | Engines, gearboxes |
| Colour / paint code | Body parts | White, paint code 040 | Body panels, mirrors |
| Quantity | Yes | 1 (auctions normally single item or a set) | |
| Includes | Optional | "with bulbs and wiring", "set of 4" | Avoids disputes |
| Warranty / returns | Optional | 7-day return, 3-month warranty | Trust |

## 4.5 Step 4 – Photos and description

- **3 to 10 photos**, JPG/PNG/WEBP, max 5 MB each (front, back, labels/part number, any damage close-up).
- Description (free text, optional).
- **Title is built automatically** so all titles look the same and contain the search words:
  *"2016–2020 Toyota Hilux Front Bumper – Genuine OEM – Used (Grade A)"*. The seller can add a few words but not remove the generated part.

## 4.6 Step 5 – Location and delivery

| Field | Required | Notes |
|---|---|---|
| Item location | Yes | Seller's primary location by default (already built) |
| Delivery options | Yes – at least one | ☐ **Pickup** ☐ **Shipping** – one or both. ("Buyer arranges courier" was removed; listings that had it now offer Pickup. "Seller ships" is now called Shipping.) |
| Exact pickup address | Optional, Pickup only | Pickup is always at the item location. Buyers only see the **city** ("Pickup in Hyderabad"); the exact address is shown to the buyer on the order after the sale |
| Shipping fee | – | **No manual fee.** The charge is calculated for each buyer (size class + distance + the country's rates, §15) |
| Size class | Yes if Shipping | Small (parcel – bulb, filter), Medium (headlamp), Large (bumper, door, bonnet), Freight (engine, gearbox). Pre-selected from the part type; the seller can change it. Vehicles are always **Vehicle** |
| Ships within | – | The seller's country (the only option for now): shipping addresses must be in that country |
| Weight | Optional | Helps courier quotes |

**How buyers see it:** cards show "📍 Pickup in Hyderabad (free) · 🚚 Shipping", "📍 Pickup only · Hyderabad" or "🚚 Shipping only";
the listing detail adds the estimate for the buyer's selected location – "Shipping to Kukatpally: ~₹350 (estimate)" – or why shipping
isn't possible ("Ships within India only", "Too far to ship …"). Search has a **Delivery** filter (Pickup / Shipping).

**After the sale** (auction won or direct sale bought):

| Listing offered | Order |
|---|---|
| Pickup only | Pickup – no choice. The buyer sees the exact pickup address |
| Shipping only | Shipping – no pickup option. The buyer must add a shipping address before the seller can ship |
| Both | The buyer chooses Pickup or Shipping on the order screen |

- **Delivery location + address:** the buyer first picks the delivery location – one of their saved locations or the current GPS
  position (coordinates only, no paid maps API) – and sees the exact charge; then the address text: name, phone, address line 1 and 2,
  city, postcode / PIN / ZIP / Eircode (Nigeria optional), country (fixed to the seller's). Saved to the buyer's **address book** (one
  default, with its coordinates) and offered first next time; the order keeps its own copy.
- **Order total:** part price + winning bid (+ the calculated shipping charge when Shipping; Pickup = 0), e.g. "Part price ₹14,000 +
  winning bid ₹100 + shipping ₹350 (22 km) = ₹14,450" or "… + pickup free = ₹14,100". Direct sales: price × quantity (+ shipping).
  The charge is **fixed on the order** when the buyer chooses Shipping: later rate changes don't change it.
- **Order status:**
  - Pickup: Sold → Ready for pickup (seller) → Picked up (seller) → Completed (buyer confirms)
  - Shipping: Sold → Shipped (seller; courier name and tracking number optional) → Delivered (buyer confirms) → Completed
  - The seller may skip steps but never go back; the buyer can confirm at any time. Confirming the delivery completes the order.
  - Shown in Orders ("Track order"); every step notifies the other party.

# 5. Adding a vehicle

(See the flow diagram in the Word version of this document.)

Two cases: **selling the whole vehicle** at auction, or **parting it out** (the vehicle is registered once as a "donor", then each part is listed with the fitment already filled in).

| Group | Fields | Required |
|---|---|---|
| Identity | VIN / chassis number (kept private, last 6 shown), Year, Make, Model, Trim / Variant, Body type, Fuel, Engine size, Transmission, Drivetrain, Steering side, Colour | Year, Make, Model, Fuel, Transmission required; VIN recommended |
| Condition | Running status (Runs & drives / Runs, not drivable / Does not run), Mileage + unit (km / mi), Damage type (none, accident, flood, fire, mechanical), Damage areas (front, rear, left, right, roof, underbody), Keys available, Number of owners, Service history | Running status, mileage, damage type |
| Papers (by country) | **India:** RC available, NOC, insurance valid until. **USA:** title status (clean, salvage, rebuilt). **Nigeria:** customs papers, registration, foreign-used ("Tokunbo") or Nigerian-used | Yes for the seller's country |
| Photos | Guided, min 8: front, rear, left, right, interior, dashboard + odometer, engine bay, damage close-ups | Yes |
| Location | Primary location by default; inspection allowed (yes / no) | Yes |

# 6. Auction details (parts and vehicles)

These follow the auction rules agreed earlier (reserve, increment, seller early-accept, grace period).

| Field | Required | Notes |
|---|---|---|
| Starting price | Yes | |
| Reserve price | Optional | Hidden from buyers; "Reserve not met" shown |
| Bid increment | Auto | Suggested from price band; seller can change |
| Buy Now price | Optional | Ends auction immediately if used |
| Duration | Yes | 1, 3, 5, 7 or 10 days (or a set end date/time) |
| Currency | Auto | INR / USD / NGN from the seller's country |
| Early accept / grace period | As agreed | Seller can accept the highest bid early; 24-hour grace to accept below reserve |
| Inspection | Vehicles | Allowed / not allowed before bidding |

# 7. How search uses these details

(See the flow diagram in the Word version of this document.)

## 7.1 Understanding what the buyer typed

| Buyer types | Detected | Search does |
|---|---|---|
| 2016 hilux front bumper | year 2016, Toyota (from model), Hilux, part type Bumper, position Front | Parts that fit a 2016 Toyota Hilux + Front Bumper |
| bonnet for swift | part type Bonnet (Hood), Maruti Suzuki Swift (any year) | All Swift bonnets; prompt "Which year?" chip |
| 52119-0K935 | Part number | Exact match on OEM / interchange numbers (dashes and spaces ignored) |
| 265/65 r17 tyre | Spec: tyre size | Spec-based tyres with that size |
| dash cam | Part type Dash cam (universal) | All dash cams nearby |
| beemer 330i headlight | BMW (alias), 3 Series, trim 330i, Headlamp | BMW 3 Series 330i headlamps |

**How words are recognised:** the master data supplies the dictionary – make names and aliases (Chevy → Chevrolet, Merc → Mercedes-Benz, Beemer / Bimmer → BMW, VW → Volkswagen), model names, part-type names and synonyms, positions (front, rear, left, right, LH, RH). A model name alone is enough to know the make. Four-digit numbers between 1950 and next year are years; "2015-2018" is a year range. Anything not recognised is used as normal text search.

## 7.2 Filtering and ranking

- **Filter:** (parts whose fitment covers the vehicle) **or** (universal / spec-based parts of that part type) – **and** inside the selected location's radius (already built) – **and** any chips the buyer picks (category, condition, brand type, price, distance, delivery: Pickup / Shipping).
- **Rank:** exact part number → exact vehicle + part type → make + model match → universal / spec-based → text-only; ties broken by nearest first.
- **Badges** on each result: **"Fits your 2016 Toyota Hilux"**, **"Universal"**, or **"Check fitment"** (when the buyer gave no year).
- **No results:** relax step by step – drop year to the nearest range, drop trim, then suggest a bigger radius – and say so ("No exact match – showing Hilux 2015–2018").

## 7.3 My Garage

Buyers save their vehicles (year, make, model, optional trim / engine) in **Profile → My Garage**. Choosing one in the search **filter
sheet** ("My Garage" section) applies the vehicle filter to every parts search, so a buyer can just type "bumper". (The garage chips and
"Add your vehicle" were removed from Home – listings are added only from Sell.)

## 7.4 How a listing is stored in the search index

Meilisearch (already in the backend) supports synonyms, typo tolerance, filterable attributes, facets and custom ranking rules, so each listing is stored with ready-made "fitment keys" that are fast to filter:

| Index field | Example value |
|---|---|
| listingType | part |
| partType, category | Bumper, Body – Exterior |
| fitmentMode | vehicle-specific |
| fitKeys | toyota · toyota\|hilux · toyota\|hilux\|2016 · … \|2020 (one key per year in the range) |
| partNumbers | 521190K935 (normalised) |
| specs | tyreSize:265/65R17 |
| condition, grade, brandType, position | used, A, oem, front |
| price, currency, location | 300, USD, lat/lon |
| deliveryOptions, shippingFee, pickupCity | [Pickup, Shipping], 500, Hyderabad |

Searching "2016 hilux bumper" then becomes a simple filter: `fitKeys = "toyota|hilux|2016" OR fitmentMode IN [universal, spec]`, plus the part type and radius.

## 7.5 Home: Parts / Vehicles, Auction / Direct sale, counts

Top of Home, applied to everything below **and** to every search (Home and Market share one set of filters):

1. **What:** [ Parts ] [ Vehicles ] (default Parts).
2. **How:** [ All ] [ Auction ] [ Direct sale ].

Below them:

- **Parts:** the part **categories** as tiles with the number of parts available, e.g. "🛑 Brakes (24)", "💡 Lighting (12)" – inside the
  listings the buyer can get (§7.7: pickup-only inside the radius, shipping country-wide) and the Auction / Direct sale choice; categories with 0 are hidden. A tile opens Market filtered by that
  category, with its **part types** as chips with counts ("Brake pads (5)").
- **Vehicles:** the total ("128 vehicles near you") and all available vehicles, with **make** chips and counts ("Toyota (12)") to
  narrow down; Market then shows models as chips.

The numbers are **Meilisearch facet counts** from the same query as the results (never counted in the app). Each document has
`listingType` (Part / Vehicle), `_geo` (exact radius with `_geoRadius`), `makes`, `models` and `years`. A chosen category / part type /
make keeps its siblings with their counts (a second facet query without that one filter); what the typed text means always counts.

## 7.6 Search + filters together (the bug that was fixed)

**What went wrong:** suggestions appeared, but the chosen filters weren't applied.

- Home's search box turned the picked chips into plain text and opened Market, and Market **reset the chips** when it received the
  text – so structured filters were lost and Home's choices (Parts / Vehicles, Auction / Direct sale) were never sent.
- Market's category / model pills and counts came from an older endpoint (`/search/filters`) that **ignored the search text and every
  filter**, so the counts didn't match the results.
- Suggestions didn't know whether the buyer was looking for parts or vehicles.

**Now:**

- Home and Market share one filter state; **every** search request carries all active filters: Parts / Vehicles, Auction / Direct
  sale, category / part type, location radius (or country), My Garage and the filter-sheet values, plus the text and chips.
- Suggestions are limited to the current choice (Vehicles → vehicles only; Parts → parts, part numbers and "Parts for Toyota Hilux").
- Picking a suggestion **adds** its chips to the existing ones (a chip of the same kind is replaced); filters stay.
- Result counts and category counts come from the same query, so they update with the search.
- Clearing the search (✕) keeps the filters; **Clear all** clears the search and the filters (Parts / Vehicles stays).

## 7.7 Which listings a buyer sees (pickup radius, shipping country-wide)

The listing's delivery options decide it – the same rule everywhere listings appear (Home tiles and counts, vehicle total and
make counts, live listings, Market results, suggestions, "See All", a seller's profile seen by a buyer, the part-request
auto-match):

| Listing delivery | Shown to the buyer when |
|---|---|
| Pickup only | the item is **inside the buyer's radius** |
| Shipping only | the item is in the **buyer's country** – any distance |
| Pickup + Shipping | the item is in the **buyer's country** – any distance |

Never a listing from another country (shipping stays within the seller's country). Older listings without delivery options
count as pickup only.

**Where:** in the Search service's Meilisearch filter (never in the app), `ListingSearchEndpoints.VisibilityFilter`:

```
(offersShipping = false AND country = "US" AND _geoRadius(lat, lon, radius))
OR (offersShipping = true AND country = "US")
```

The buyer's country is the `country` the app sends (the selected location's country), or the country at lat/lon
(`Contracts.ListingVisibility.CountryAt`). Without lat/lon (location off): `country = "US"` – the whole country. The same rule
in C# (`ListingVisibility.IsVisible`) is used by the part-request matcher.

**Index fields** (computed in `Item.Prepare`, filterable, applied and checked on every start like the other settings):
`country` (CountryCode, or from the coordinates on older listings), `offersPickup`, `offersShipping`, `_geo`.
`Item.CurrentIndexVersion` = 3, so every existing listing is fetched and re-indexed once at startup.

**Counts = results:** the category / part type / make facets come from the same query. The Delivery filter's counts are two
extra count queries, because they can't come from the `deliveryOptions` facet (a far pickup + shipping listing is visible, but
not for pickup):

- **Pickup** = listings offering pickup **inside the radius** (pickup only and pickup + shipping).
- **Shipping** = listings that ship, anywhere in the country.

The filter sheet's **Distance** limits everything (pickup and shipping). **Nearest first** sorts by distance – every result has
its `distanceKm`. Each result also has `withinRadius` (pickup offered to this buyer).

**Suggestions** (`/search/suggest?lat=&lon=&radiusKm=&country=`): what comes from listings (part numbers, the years a vehicle's
listings cover) uses the same filter. Category / part type / make suggestions come from the catalogue, as before.

**In the app** (cards and the listing):

- inside the radius: "📍 Pickup · 12 mi away" and / or "🚚 Shipping ~$12"
- outside the radius, it ships: "🚚 Ships to you ~$95 · 1,370 mi away" (no pickup badge – pickup is too far)
- shipping can't be worked out for this buyer (e.g. over the size class's maximum distance): the listing still shows,
  "🚚 Shipping not available to your location"; Buy now / Place bid explain why (and stop when the item only ships).

The charges come from one call for the page of results, `POST /shipping/estimates` (at most 50 listings). At checkout
(Orders → "How do you want to receive it?") an item outside the radius has **Shipping** pre-selected; Pickup can still be
chosen ("… 640 mi away, outside your 200 mi radius").

**Shipping range:** the seeded maximum road distance (straight line × 1.3) covers every country, so normal in-country
shipping always works: USA freight / vehicles 4,000 mi (corner to corner ≈ 3,550 mi; parcels go by USPS with no limit),
India 4,000 km, Nigeria 1,800 km, Ireland 700 km, all sizes. Seeded rows that are still placeholders / estimates get the new
values at startup; rates an admin has set are left alone.

## 7.8 The radius (default 200, chosen per location)

- **One setting** for the backend, `Contracts.RegionRules`: `DefaultRadiusValue = 200`, `MinRadiusValue = 5`,
  `MaxRadiusValue = 1,000`, in the location's unit (**miles in the USA**, km elsewhere); and for the app, `src/utils/geo.ts`
  (`DEFAULT_RADIUS`, `MIN_RADIUS`, `MAX_RADIUS`, `RADIUS_CHOICES` = 25 / 50 / 100 / 200 / 500). The Search service, the
  part-request audience and matcher and `/geo/reverse` all use it (no other defaults).
- **Stored with the location** (IdentityService `UserLocations.RadiusKm`, whole km – every value 5–1,000 mi comes back exactly).
  The API takes `radiusValue` in the location's unit and returns `radiusValue`, `radiusUnit` and the exact `radiusKm`. Outside
  5–1,000 → 400 `INVALID_RADIUS`. Older apps' `radiusKm` is still accepted (pulled into range).
- **Where it is chosen:** *Use my current location* (a radius sheet), *Add / Edit address* (SEARCH RADIUS), and Home's location
  line ("📍 Houston, USA · 200 mi ▾" → sheet; `PUT /api/me/locations/{id}/radius`, or the GPS location is saved with it).
  The lists and counts update at once (the app uses the new radius before the server answers).
- **GPS without a saved radius** → the default 200 for the country the phone is in. A GPS radius saved in the USA (200 mi)
  reads as 200 km after moving to India – the number follows the location's unit.
- **Old data:** migration `LocationRadius200` changes saved locations with the previous default – 60 km, or 60 mi (96 / 97 km)
  in the USA – to 200. Those values were never a radius choice in the app; everything else (e.g. 50 km, which was both an
  older default and a chip) is kept.

# 8. Data model

(See the flow diagram in the Word version of this document.)

- **Master data** (managed by admins): Make, Model, VehicleConfig, Category, PartType (with synonyms, fitment mode, position and spec fields).
- **Listing** is shared by parts and vehicles; **Auction** holds pricing and timing.
- A part listing has **PartDetails**, any number of **PartFitment** rows (year ranges), **PartNumber** rows and **PartSpec** values.
- A vehicle listing has **VehicleDetails**.
- **GarageVehicle** stores buyers' saved vehicles.
- In the current backend these map onto the Auction service (listing, item, auction) and the Search service index (section 7.4).

## 8.1 Where vehicle master data comes from

| Source | Covers | Cost | Use |
|---|---|---|---|
| NHTSA vPIC | Makes, models by year and VIN decoding for vehicles sold in the US (1981 onward) | Free, no registration | US makes/models; VIN decode (also useful for US-spec foreign-used vehicles in Nigeria) |
| Our own curated list | Popular Indian and Nigerian-market makes/models/variants (Maruti Suzuki, Tata, Mahindra, Hyundai India, Toyota Africa models …) | Staff time | Seed India and Nigeria |
| Seller "Other – request to add" | Anything missing | – | Admin approves, then it joins the list |
| Licensed fitment data (ACES) | Detailed fitment for millions of parts | Paid licence | Later, if the business needs it |

# 9. Rules and checks

- Fitment rows: year range ≤ 30 years; no duplicate rows; up to 50 rows per listing.
- Part numbers stored as typed **and** normalised (uppercase, no spaces or dashes).
- A vehicle-specific part cannot be published without at least one fitment row; a spec-based part needs its spec fields; universal needs neither.
- Position is required only for part types that have one.
- Title prefix is generated and can't be removed; banned words / contact details blocked in titles and descriptions.
- VIN is never shown in full publicly.
- Photos: 3–10 for parts, 8+ for vehicles; max 5 MB each.

# 10. Examples

| | Used bumper | Tyre | Dash cam |
|---|---|---|---|
| Part type | Body – Exterior > Front bumper | Wheels & Tyres > Tyre | Accessories > Dash cam |
| Mode | Vehicle-specific | Spec-based | Universal |
| Captured | Toyota Hilux 2016–2020, Front, Genuine OEM, Used grade A, white | 265/65 R17, 112T, 2024, New, Aftermarket (Bridgestone) | Brand, New |
| Found by | "2016 hilux bumper", "hilux front bumper", OEM number, My Garage = 2018 Hilux | "265 65 17", "tyre for 2018 hilux"* | "dash cam", any vehicle |

\* Showing tyres for a vehicle (vehicle → tyre size) needs a vehicle-to-spec table; planned for phase 2.

# 11. Phased plan

| Phase | Includes |
|---|---|
| **Phase 1 (build now)** | Category / part-type list with fitment modes and synonyms; Add Part flow (fitment rows with year ranges, specs, part details, photos, location, auction); Add Vehicle (whole vehicle + parting out); search understanding (years, makes, models, aliases, part types, synonyms, part numbers); fitment keys in the search index; badges; My Garage filter |
| **Phase 2** | VIN decode (vPIC); copy fitment by OEM number; vehicle → tyre / bulb / battery spec lookup; bulk upload by spreadsheet for big sellers; admin screens for master data and "request to add" |
| **Phase 3** | Licensed fitment data; photo-based part suggestions; price suggestions from past auctions |

# 12. Questions to confirm

1. Are **vehicles** sold by auction only, or can parts also have a fixed "Buy Now only" listing?
2. Should **Buy Now** be allowed on auctions?
3. Which **countries' vehicle lists** should be seeded first (India, Nigeria, USA – all at once)?
4. Who maintains the **master data** (makes, models, part types) – admin team or developers?
5. Should **used grade A / B / C** be mandatory for used parts?
6. ~~Is shipping handled by the platform?~~ Decided: the seller offers Pickup and / or Shipping (within their country); the shipping
   charge is calculated from size class and distance (§15); the buyer chooses after the sale; couriers are booked by the seller
   (tracking number optional). See §4.6. **Still open:** the real rates per country (the seeded ones are placeholders).
7. Should the **VIN** be mandatory for whole-vehicle auctions?
8. Are **lots** (several parts in one auction, e.g. "set of 4 wheels") needed in phase 1?

# 13. Accounts and roles – who can list

Listings come only from **sellers**. Every account has exactly one role, kept in the Identity service and sent in the
access token as `"role"`. The app chooses its screens from it and **every service checks it again** (the app is never trusted).

| Role | How an account gets it | Can do |
|---|---|---|
| **Buyer** | Signing up in the app – email, Google or Apple. Always buyer, there is no role choice. | Browse and search, bid, Buy now, chat, orders as buyer, reviews, saved locations / addresses, My Garage |
| **Seller** | Created by an admin (`POST /api/admin/sellers`), or a buyer upgraded by an admin (`make-seller`). No seller sign-up in the app ("Want to sell? Contact us" emails the team). | Everything a buyer can, plus: Sell (add part / vehicle, auction or direct sale), edit / publish / cancel / relist, photos, accept & close bids, Sales (mark ready / handed over / shipped), seller profile with reviews |
| **Admin** | Seeded from secrets for local testing; no admin screens yet (API + Swagger) | Seller accounts (create, invite, update, disable / enable, list, upgrade a buyer), catalogue approvals, hiding reviews |

**New seller:** the admin enters full name, email, phone, country and optionally business name and city. The seller
gets an email with a one-time 6-digit code (72 hours) and sets a password in the app (Login → "Got a seller invite?
Set your password"), then logs in normally with email + password + OTP. A disabled account can't log in and its
refresh tokens stop working.

**Role changes:** the token carries the role, and a token refresh re-reads it. The app also checks `/me` when it comes
back to the foreground and refreshes the token at once if the role changed, so an upgraded buyer sees **Sell** without
signing in again or reinstalling.

**Checks on the backend** (403 with a message the app can show):

| Action | Rule | Code |
|---|---|---|
| Create, save, publish, add a note, cancel, relist a listing; add / remove / reorder photos; older `/api/auctions` create, update, delete | role seller | `SELLER_ONLY` |
| Accept a bid (close the auction) | role seller **and** the auction's own seller | `SELLER_ONLY` / `NOT_AUCTION_SELLER` |
| Order fulfilment steps (ready for pickup, picked up, packed, shipped …) | the order's seller (buyer: delivered / completed) | `NOT_ORDER_PARTY` |
| `/api/admin/*` | role admin | `ADMIN_ONLY` |
| Buy now, bid | any signed-in account, never on your own listing | – |

**Existing data:** when roles were introduced every existing account became a buyer, and accounts that already had
listings became sellers (a one-time step: Identity asks the Auction service for the usernames that have listings).

# 14. Direct sale

On Sell the seller chooses **Auction** or **Direct sale (fixed price)**. Direct sale: price, quantity (default 1), the same photos,
details and delivery options. Buyers see **"Buy now – ₹4,500"** instead of bidding; buying creates an order straight away (the same
order / fulfilment flow as a won auction), the quantity goes down and the listing shows **Sold out** at 0. A seller can't buy their own
item. Existing auctions stay auctions. Home's Auction / Direct sale filter applies to everything.

# 15. Shipping charges

- **Pickup** is always free (charge 0).
- **Shipping** is calculated by the backend – the app never sends a price:
  - **Size class** of the item: Small, Medium, Large, Freight, Vehicle. Each part type has a default (bulb Small, headlamp Medium,
    bumper / door Large, engine / gearbox Freight) the seller can change; vehicles are Vehicle.
  - **Distance** from the item location to the buyer's delivery location (a saved location or GPS): straight line (Haversine) × **1.3**
    road factor, in km (miles in the USA).
  - **Price** = base fee + distance × rate per km / mile for the size class, rounded to a whole number, kept between the size class's
    minimum and maximum.
  - Not available when the distance is above the size class's **maximum distance**, or the buyer is in another country – the app
    says why.
- **Rates** live in the `ShippingRates` table per country and currency (country, size class, base fee, rate per unit, min fee, max fee,
  max distance). Admin API: `GET /api/shipping/rates`, `PUT /api/shipping/rates/{country}/{sizeClass}` (admin only).
- **Seeded PLACEHOLDER values** (marked `isPlaceholder` until an admin replaces them – not real courier prices):

| Country | Unit | Small | Medium | Large | Freight | Vehicle |
|---|---|---|---|---|---|---|
| India (₹) | km | 60 + 1/km (max 600, ≤ 2,500 km) | 120 + 2/km (max 1,500, ≤ 2,500) | 300 + 5/km (max 4,000, ≤ 2,000) | 800 + 12/km (max 15,000, ≤ 1,500) | 2,000 + 25/km (max 60,000, ≤ 1,500) |
| USA ($) | mile | 8 + 0.15/mi (max 60, ≤ 3,000 mi) | 15 + 0.30 (max 150, ≤ 3,000) | 40 + 0.80 (max 400, ≤ 2,500) | 120 + 2.00 (max 1,500, ≤ 2,000) | 250 + 2.50 (max 3,000, ≤ 2,000) |
| Nigeria (₦) | km | 1,500 + 30 (max 20,000, ≤ 1,200) | 3,000 + 60 (max 50,000, ≤ 1,200) | 8,000 + 150 (max 120,000, ≤ 1,000) | 25,000 + 400 (max 400,000, ≤ 800) | 60,000 + 800 (max 1,200,000, ≤ 800) |
| Ireland (€) | km | 6 + 0.15 (max 40, ≤ 500) | 12 + 0.30 (max 80, ≤ 500) | 30 + 0.80 (max 200, ≤ 500) | 90 + 2.00 (max 600, ≤ 500) | 200 + 2.50 (max 1,200, ≤ 500) |

  The minimum fee is the base fee in every row.

- **Where it shows:** listing detail ("Shipping to Kukatpally: ~₹350 (estimate)" for the buyer's selected location; "Pickup: free");
  Buy now / after winning the buyer chooses Pickup or Shipping (if both are offered) and sees the exact charge before confirming;
  the order total = item price (+ winning bid for auctions) + shipping charge (0 for pickup); the order shows the breakdown to both
  sides. Endpoints: `GET /api/shipping/estimate?listingId&lat&lon&country` (estimate) and `POST /api/orders/{id}/delivery-method`
  with the address and `latitude` / `longitude` (calculates and **stores** the charge on the order).
- Orders from before this change keep the seller's manual fee.

## 15.1 USA: USPS Ground Advantage (official prices, no API keys needed)

In the USA, parts that fit USPS are priced like **USPS Ground Advantage – Retail**, from the official price list
(**USPS Notice 123, edition effective 4 Oct 2026**, stored as `AuctionService/Data/Seed/usps-ground-advantage-retail.csv`):

- **Package:** weight (lb) and box size (in) on the listing – pre-filled for the part type (e.g. headlamp 8 lb, 24 × 16 × 14 in;
  bulb 0.5 lb; unknown types from the size class), editable by the seller on Sell.
- **Zone 1–8** from the straight-line distance between the item and the buyer's delivery location (≤ 50 mi zone 1, ≤ 150 zone 2,
  ≤ 300 zone 3, ≤ 600 zone 4, ≤ 1,000 zone 5, ≤ 1,400 zone 6, ≤ 1,800 zone 7, farther zone 8). USPS uses a ZIP-to-ZIP zone chart,
  so near a band edge the zone can differ by one.
- **Weight step:** 4, 8, 12, 15.999 oz, then whole pounds up to 70 lb (rounded up). Over one cubic foot the **greater of actual and
  dimensional weight** (L × W × H ÷ 166, rounded up) is charged.
- **Oversized:** length + girth over 108 in (up to 130 in) → the oversized price for the zone.
- **Non-standard fees:** longest side over 22 in +$4.50, over 30 in +$10.00; over 2 ft³ +$21.00.
- The total is **rounded up to a whole dollar** (order amounts are whole numbers).
- **Too big for USPS** (over 70 lb or 130 in length + girth), **Freight** and **Vehicle**: delivered anywhere in the US (up to
  3,500 road miles) at **US industry-average estimates**, labelled **"estimated rate"** in the app and on the order until a carrier
  is chosen (then an admin enters the carrier's rates and the label disappears):

  | | Formula (road miles = straight line × 1.3) | Min / max | Check | Sources |
  |---|---|---|---|---|
  | Freight (LTL, 1 pallet ≤ ~500 lb) | $250 + $0.35/mi | $250 / $1,500 | 200 mi ≈ $320; 1,000 mi ≈ $600 | Warp LTL rate index ($532–582 national average, Aug–Sep 2026); ParcelPath and Red Stag 2026 guides |
  | Vehicle (open carrier, running car) | $350 + $0.70/mi | $400 / $2,800 | 300 mi ≈ $560; 1,000 mi ≈ $1,050; New York → Los Angeles ≈ $2,580 | FreightWaves 2026; Ship A Car 2026 ($2,200–2,800 coast to coast); Kelley Blue Book (~$1/mi at 1,000 mi); AmeriFreight |

  Real prices vary with weight / freight class, liftgate and residential delivery (freight), and enclosed transport, non-running
  vehicles and season (vehicles: +$150–300 non-running, +30–60% enclosed).
- Quotes and orders show the service and zone ("$12 · USPS Ground Advantage, zone 2 · 2 lb"); the order stores the charge,
  service and zone.

**Later, with USPS API keys:** Domestic Prices API (exact ZIP-to-ZIP prices), Addresses API (validate the buyer's address),
Tracking API; labels / carrier pickup / adjustments need a USPS business account (CRID, MID, funded EPS account).
When USPS publishes a new Notice 123, replace the CSV (same format) and restart AuctionService.

# 16. Part requests (custom orders) – buyers ask, sellers are told

1. **A buyer requests a part they can't find** (search found nothing → "Create a custom order": part, vehicle, part number,
   budget, their area). **The sellers near the buyer are notified** – within the
   buyer's radius (200 km / 200 mi by default) when the buyer's location is known, otherwise anywhere in the buyer's country, and
   **never a seller in another country** (a seller whose location / country isn't known isn't told). Live in the app ("🔎 A buyer is looking for a part – Front bumper –
   2016 Toyota Hilux (Hyderabad)") and by push when the app is closed. Sellers see all open requests in **Buyer requests**
   (Profile, or tap the notification) – the same rule decides which requests they see: what is wanted, the vehicle, the buyer's **city** and budget – never the buyer's name or exact
   location. "I have this – list it" opens Sell. Buyers aren't notified, and a seller isn't told about their own request.
2. **A seller lists a matching part** (auction or direct sale) → **every buyer with a matching open request is notified** ("A part
   matching your request is now available", live and by push; tap → the listing) and the request becomes *Matched*. A match is
   the same part number, or the same part type that fits the buyer's vehicle, **and a listing the buyer would see** (§7.7):
   pickup only → within the buyer's radius; it ships → anywhere in the buyer's country; never another country.

How it flows: AuctionService publishes `PartRequestCreated` → NotificationService sends it to the SignalR group of connected sellers
(joined from the token's role) and ChatService pushes it to every phone registered by a seller account; on publish,
`CustomOrderMatcher` → `CustomOrderMatched` → the buyer (live + push). Endpoint for sellers: `GET /api/partrequests/open`.

# Sources

- eBay Motors – What is fitment (Year, Make, Model, Trim, Engine): https://pages.ebay.ca/motors/listing-fitment/
- ACES & PIES beginner's guide (fitment vs product data): https://blog.pcfitment.com/automotive-data-standards-aces-pies-beginners-guide/
- Walmart Marketplace – Automotive fitment (ACES, part type, part number): https://developer.walmart.com/us-marketplace/docs/automotive-fitment
- NHTSA vPIC API (VIN decode, makes/models; free, US market since 1981), via the vpic-api client: https://pypi.org/project/vpic-api
- Car-Part.com – used part grading (A/B/C by damage units): https://products.car-part.com/newsletters/scoop/cps20080229.html
- Meilisearch – index settings (synonyms, typo tolerance, filterable attributes, ranking rules): https://www.meilisearch.com/docs/reference/api/settings/list-all-settings.md
