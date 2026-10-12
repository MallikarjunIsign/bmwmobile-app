---
title: "OKOH Parts – Part, Vehicle and Auction Listing & Search Design"
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
| Shipping fee | Yes if Shipping | Amount in the listing currency, or **Free shipping** (fee 0). Added to the order total when the buyer chooses Shipping |
| Size class | Yes if Shipping | Small (parcel), Medium, Large (bumper, door), Freight (engine, gearbox) |
| Ships within | – | The seller's country (the only option for now): shipping addresses must be in that country |
| Weight | Optional | Helps courier quotes |

**How buyers see it** (cards and the listing detail): "📍 Pickup in Hyderabad · 🚚 Shipping ₹500", "📍 Pickup only · Hyderabad" or
"🚚 Shipping only · Free shipping". Search has a **Delivery** filter (Pickup / Shipping).

**After the sale** (auction won or direct sale bought):

| Listing offered | Order |
|---|---|
| Pickup only | Pickup – no choice. The buyer sees the exact pickup address |
| Shipping only | Shipping – no pickup option. The buyer must add a shipping address before the seller can ship |
| Both | The buyer chooses Pickup or Shipping on the order screen |

- **Shipping address:** name, phone, address line 1 and 2, city, postcode / PIN / ZIP / Eircode (Nigeria optional), country (fixed to
  the seller's). Saved to the buyer's **address book** (one default) and offered first next time; the order keeps its own copy.
- **Order total:** part price + winning bid (+ shipping fee when Shipping), e.g. "Part price ₹14,000 + winning bid ₹100 + shipping ₹500
  = ₹14,600". Direct sales: price × quantity (+ shipping).
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

Buyers save their vehicles (year, make, model, optional trim / engine). The app already shows "Filter by your garage" chips; choosing one applies the vehicle filter to every search and to the Home list, so a buyer can just type "bumper".

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
6. ~~Is shipping handled by the platform?~~ Decided: the seller offers Pickup and / or Shipping (fee or free, within their country),
   the buyer chooses after the sale; couriers are booked by the seller (tracking number optional). See §4.6.
7. Should the **VIN** be mandatory for whole-vehicle auctions?
8. Are **lots** (several parts in one auction, e.g. "set of 4 wheels") needed in phase 1?

# Sources

- eBay Motors – What is fitment (Year, Make, Model, Trim, Engine): https://pages.ebay.ca/motors/listing-fitment/
- ACES & PIES beginner's guide (fitment vs product data): https://blog.pcfitment.com/automotive-data-standards-aces-pies-beginners-guide/
- Walmart Marketplace – Automotive fitment (ACES, part type, part number): https://developer.walmart.com/us-marketplace/docs/automotive-fitment
- NHTSA vPIC API (VIN decode, makes/models; free, US market since 1981), via the vpic-api client: https://pypi.org/project/vpic-api
- Car-Part.com – used part grading (A/B/C by damage units): https://products.car-part.com/newsletters/scoop/cps20080229.html
- Meilisearch – index settings (synonyms, typo tolerance, filterable attributes, ranking rules): https://www.meilisearch.com/docs/reference/api/settings/list-all-settings.md
