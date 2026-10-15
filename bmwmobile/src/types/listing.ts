/** Types for the listing model (design doc §3–§8): selling, search, bidding and orders. */
import type { SaleType } from './models';

export type ListingKind = 'CommonPart' | 'VehicleSpecificPart' | 'Vehicle';
export type FitmentMode = 'Universal' | 'SizeBased' | 'VehicleSpecific';
export type DeliveryOption = 'Pickup' | 'Shipping';

export interface SpecField {
  key: string;
  label: string;
  type: 'number' | 'text' | 'select';
  unit?: string | null;
  options: string[];
  required: boolean;
}

export interface PartType {
  id: number;
  name: string;
  categoryId: number;
  category: string;
  subCategory?: string | null;
  fitmentMode: FitmentMode;
  hasPosition: boolean;
  hasSide: boolean;
  isBodyPart: boolean;
  synonyms: string[];
  importantFitmentFields: string[];
  specFields: SpecField[];
  matchedSynonym?: string | null;
  /** Shipping size class the Sell form starts with (the seller can change it). */
  defaultSizeClass?: SizeClass;
  /** US shipping: the package the Sell form starts with (lb / inches). */
  defaultWeightLb?: number;
  defaultLengthIn?: number;
  defaultWidthIn?: number;
  defaultHeightIn?: number;
}

export interface Category {
  id: number;
  name: string;
  subCategories: Category[];
  partTypes: PartType[];
}

export interface Make {
  id: number;
  name: string;
  aliases: string[];
  countries: string[];
  isApproved: boolean;
}

export interface VehicleModel {
  id: number;
  makeId: number;
  make: string;
  name: string;
  aliases: string[];
  bodyType?: string | null;
  isApproved: boolean;
}

export interface Fitment {
  yearFrom: number;
  yearTo: number;
  makeId?: number | null;
  make: string;
  modelId?: number | null;
  model: string;
  trim?: string | null;
  engine?: string | null;
  fuel?: string | null;
  transmission?: string | null;
  drivetrain?: string | null;
  steeringSide?: string | null;
  market?: string | null;
  note?: string | null;
}

export interface PartDetails {
  brandType?: string | null;
  brand?: string | null;
  position?: string | null;
  side?: string | null;
  condition?: string | null;
  usedGrade?: string | null;
  donorMileage?: number | null;
  colour?: string | null;
  paintCode?: string | null;
  quantity?: number | null;
  includes?: string | null;
  warranty?: string | null;
}

export interface PartNumberItem {
  value: string;
  kind: 'OEM' | 'Aftermarket';
  normalized?: string | null;
}

export interface VehicleDetails {
  trim?: string | null;
  bodyType?: string | null;
  fuel?: string | null;
  engineSize?: string | null;
  transmission?: string | null;
  drivetrain?: string | null;
  steeringSide?: string | null;
  vin?: string | null;
  runningStatus?: string | null;
  mileageUnit?: string | null;
  damageType?: string | null;
  damageAreas: string[];
  keys?: number | null;
  owners?: number | null;
  serviceHistory?: string | null;
  inspectionAllowed: boolean;
  papers: Record<string, string>;
}

export interface ListingImage {
  id: string;
  url: string;
  thumbnailUrl?: string | null;
  angle?: string | null;
  sortOrder: number;
  isPrimary: boolean;
}

/** AuctionService AuctionDto (listing detail / seller editor). */
export interface Listing {
  id: string;
  status: string;
  seller: string;
  kind?: ListingKind | null;
  itemType: string;
  fitmentMode?: FitmentMode | null;
  partTypeId?: number | null;
  partType?: string | null;
  category?: string | null;
  subCategory?: string | null;
  title?: string | null;
  titleExtra?: string | null;
  fitmentSummary?: string | null;
  description: string;
  make: string;
  model: string;
  year: number;
  yearTo?: number | null;
  color?: string | null;
  mileage?: number | null;
  countryCode?: string | null;
  currency?: string | null;
  locationName?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  /** Pickup and / or Shipping. */
  deliveryOptions: DeliveryOption[];
  sizeClass?: string | null;
  /** US shipping: package weight (lb) and box size (inches); null = the part type's default. */
  weightLb?: number | null;
  lengthIn?: number | null;
  widthIn?: number | null;
  heightIn?: number | null;
  /** Shipping fee (0 = free shipping); null without shipping. */
  shippingFee?: number | null;
  /** Shipping: the country it ships within (the listing country). */
  shipsWithin?: string | null;
  /** Pickup: the public city ("Hyderabad"). */
  pickupCity?: string | null;
  /** Seller only: the exact pickup address (only shared with the buyer after the sale). */
  pickupAddress?: string | null;
  durationDays?: number | null;
  startingPrice: number;
  currentHighBid?: number | null;
  bidCount: number;
  auctionEnd: string;
  winner?: string | null;
  soldAmount?: number | null;
  partDetails?: PartDetails | null;
  fitments: Fitment[];
  partNumbers: string[];
  partNumberItems: PartNumberItem[];
  specs: Record<string, string>;
  vehicle?: VehicleDetails | null;
  imageItems: ListingImage[];
  imageUrl: string;
  thumbnailUrl?: string | null;
  canEdit: boolean;
  saleType?: SaleType;
  sellerName?: string | null;
  /** FixedPrice: units still for sale / bought so far. */
  quantityAvailable?: number;
  quantitySold?: number;
  sellerRating?: number | null;
  sellerRatingCount?: number;
}

/** PUT /listings/{id}: only the fields sent change. */
export interface SaveListing {
  partTypeId?: number;
  titleExtra?: string;
  description?: string;
  year?: number;
  make?: string;
  model?: string;
  colour?: string;
  mileage?: number;
  partDetails?: PartDetails;
  fitments?: Fitment[];
  partNumbers?: PartNumberItem[];
  specs?: Record<string, string>;
  vehicle?: VehicleDetails;
  locationName?: string;
  latitude?: number;
  longitude?: number;
  countryCode?: string;
  deliveryOptions?: DeliveryOption[];
  sizeClass?: string | null;
  /** US shipping: package weight (lb) and box size (inches); null = the part type's default. */
  weightLb?: number | null;
  lengthIn?: number | null;
  widthIn?: number | null;
  heightIn?: number | null;
  /** Shipping: the fee; 0 = free shipping. */
  shippingFee?: number;
  /** true = free shipping; false = not free any more (enter the fee). */
  freeShipping?: boolean;
  /** Pickup, optional: the exact address (shared with the buyer after the sale). */
  pickupAddress?: string;
  partPrice?: number;
  /** Older drafts: days from publishing. A chosen auctionEnd replaces it. */
  durationDays?: number;
  /** Auction: when it ends (ISO, UTC), 1 hour to 30 days ahead. */
  auctionEnd?: string;
  saleType?: SaleType;
}

export interface MyListing {
  id: string;
  title: string;
  /** Draft, Live, EndedNoBids, Sold, Completed, Cancelled. */
  state: 'Draft' | 'Live' | 'EndedNoBids' | 'Sold' | 'Completed' | 'Cancelled';
  status: string;
  kind?: ListingKind | null;
  imageUrl?: string | null;
  partPrice: number;
  currentHighBid?: number | null;
  bidCount: number;
  auctionEnd: string;
  updatedAt: string;
  currency?: string | null;
  orderId?: string | null;
  saleType?: SaleType;
  quantityAvailable?: number;
  quantitySold?: number;
}

/** Field → messages (ASP.NET ValidationProblem). */
export type FieldErrors = Record<string, string[]>;

// ── search ──

export interface QueryChip {
  type: 'partType' | 'make' | 'model' | 'year' | 'partNumber' | 'size' | 'position' | 'category' | 'condition' | 'brandType' | 'kind';
  value: string;
  label: string;
}

/** A buyer's open part request as sellers see it (no buyer name, only the city). */
export interface OpenPartRequest {
  id: string;
  /** "Front bumper – 2016 Toyota Hilux". */
  title: string;
  partName?: string | null;
  partNumber?: string | null;
  make?: string | null;
  model?: string | null;
  year?: number | null;
  specs?: string | null;
  maxBudget?: number | null;
  locationName?: string | null;
  countryCode?: string | null;
  createdAt: string;
}

/** Home's first filter: what the buyer is looking for. */
export type ItemTypeFilter = 'Part' | 'Vehicle';

/** Shipping size class: Small, Medium, Large, Freight or Vehicle. */
export type SizeClass = 'Small' | 'Medium' | 'Large' | 'Freight' | 'Vehicle';

export interface Suggestion {
  /** fits = "Parts for Toyota Hilux 2016–2020" (a vehicle while Parts is chosen). */
  group: 'parts' | 'vehicles' | 'fits' | 'partNumbers' | 'recent';
  label: string;
  chips: QueryChip[];
  subtitle?: string | null;
}

export interface FacetValue {
  value: string;
  count: number;
}

export interface ListingSearchQuery {
  q?: string;
  /** Parts or Vehicles (Home's first filter). */
  itemType?: ItemTypeFilter;
  partType?: string;
  make?: string;
  model?: string;
  year?: number;
  category?: string;
  kind?: string;
  condition?: string;
  brandType?: string;
  minPrice?: number;
  maxPrice?: number;
  maxDistanceKm?: number;
  sort?: 'best' | 'endingSoon' | 'newest' | 'priceAsc' | 'priceDesc' | 'nearest' | 'topRated';
  /** "Seller rating 4★ & up". */
  minSellerRating?: number;
  /** Only this seller's listings (username). */
  seller?: string;
  saleType?: SaleType;
  /** Only listings that offer Pickup / Shipping. */
  delivery?: DeliveryOption;
  page?: number;
  pageSize?: number;
}

export interface SearchHit {
  id: string;
  title?: string | null;
  kind?: ListingKind | null;
  fitmentMode?: FitmentMode | null;
  partType?: string | null;
  category?: string | null;
  fitmentSummary?: string | null;
  condition?: string | null;
  usedGrade?: string | null;
  brandType?: string | null;
  brand?: string | null;
  make: string;
  model: string;
  year: number;
  seller: string;
  startingPrice: number;
  currentHighBid?: number | null;
  currentPrice: number;
  bidCount: number;
  auctionEnd: string;
  status: string;
  imageUrl: string;
  thumbnailUrl?: string | null;
  locationName?: string | null;
  distanceKm?: number | null;
  /** Inside the buyer's radius (pickup is offered to them); false = shown because it ships. Only with a location. */
  withinRadius?: boolean | null;
  badge?: string | null;
  matchTier: number;
  currency?: string | null;
  countryCode?: string | null;
  saleType?: SaleType;
  sellerName?: string | null;
  quantityAvailable?: number;
  deliveryOptions?: DeliveryOption[];
  shippingFee?: number | null;
  pickupCity?: string | null;
  sellerRating?: number | null;
  sellerRatingCount?: number;
}

export interface ListingSearchResult {
  results: SearchHit[];
  totalCount: number;
  pageCount: number;
  chips: QueryChip[];
  keywords: string;
  facets: Record<string, FacetValue[]>;
  priceMin?: number | null;
  priceMax?: number | null;
}

export interface GarageVehicle {
  id?: string;
  year: number;
  make: string;
  model: string;
  trim?: string | null;
  engine?: string | null;
  nickname?: string | null;
}

// ── bidding & orders ──

export interface MyBid {
  auctionId: string;
  myHighestBid: number;
  myLastBidTime: string;
  currentHighBid?: number | null;
  highBidder?: string | null;
  bidCount: number;
  auctionStatus: string;
  auctionEnd: string;
  state: 'Winning' | 'Outbid' | 'Won' | 'Lost';
}

/** Pickup: Sold → ReadyForPickup → PickedUp → Completed. Shipping: Sold → Shipped → Delivered → Completed. */
export type OrderStatus = 'SoldAwaitingHandover' | 'ReadyForPickup' | 'PickedUp' | 'Shipped' | 'Delivered' | 'Completed';

/** A shipping address (orders and the address book). */
export interface Address {
  name: string;
  phone: string;
  line1: string;
  line2?: string | null;
  city: string;
  /** Postcode / PIN / ZIP / Eircode. */
  postcode: string;
  countryCode: string;
  /** The delivery location (a saved location or GPS): the shipping charge uses it. */
  latitude?: number | null;
  longitude?: number | null;
}

/** A shipping charge from the backend (the app never works out a price itself). */
export interface ShippingQuote {
  available: boolean;
  /** Whole amount in the listing currency; null when not available. */
  charge: number | null;
  /** Road distance used (km, or mi in the USA). */
  distance: number | null;
  unit: 'km' | 'mi';
  currency: string;
  sizeClass: SizeClass;
  /** Why shipping isn't possible, e.g. "Ships within India only". */
  reason: string | null;
  /** The rates are still the seeded placeholders. */
  placeholderRates: boolean;
  /** US: "USPS Ground Advantage" (with zone 1–8 and the billable weight) or "Freight". */
  service?: string | null;
  zone?: number | null;
  billableWeightLb?: number | null;
  /** The USPS price list edition, e.g. "2026-10-04". */
  priceList?: string | null;
  /** An industry-average estimate, not a carrier's price (US freight / vehicle transport for now): shown as "estimated rate". */
  estimatedRate?: boolean;
}

export interface ShippingEstimate {
  listingId: string;
  pickupOffered: boolean;
  shippingOffered: boolean;
  shipping: ShippingQuote | null;
}

export interface ShippingAddress extends Address {
  id: string;
  isDefault: boolean;
}

export interface OrderStep {
  status: OrderStatus;
  label: string;
  done: boolean;
  current: boolean;
  at?: string | null;
  /** e.g. "Delhivery · Tracking AWB123" */
  note?: string | null;
}

export interface Order {
  id: string;
  auctionId: string;
  role: 'buyer' | 'seller';
  seller: string;
  buyer: string;
  otherParty: string;
  title: string;
  imageUrl?: string | null;
  /** Auction (won) or FixedPrice (bought). */
  saleType?: SaleType;
  /** Units bought (fixed price can be more than 1). */
  quantity?: number;
  /** The item: part price + winning bid, or price × quantity (shipping not included). */
  finalTotal: number;
  /** The breakdown: part price + winning bid + shipping fee (when shipped) = total. */
  partPrice?: number;
  winningBid?: number;
  /** Older apps: same as shippingCharge. */
  shippingFee?: number | null;
  /** 0 for Pickup; Shipping: the charge calculated when the buyer chose it (fixed); null while not chosen. */
  shippingCharge?: number | null;
  /** Road distance the charge used and its unit, and the item's size class. */
  shippingDistance?: number | null;
  shippingDistanceUnit?: 'km' | 'mi' | null;
  shippingSizeClass?: SizeClass | null;
  /** US: "USPS Ground Advantage" (and its zone) or "Freight". */
  shippingService?: string | null;
  shippingZone?: number | null;
  /** The charge used an industry-average estimate ("estimated rate"). */
  shippingRateEstimated?: boolean;
  total?: number;
  currency?: string | null;
  locationName?: string | null;
  /** The seller's country: shipping goes within it. */
  countryCode?: string | null;
  deliveryOptions: DeliveryOption[];
  /** Pickup / Shipping; null until the buyer chose (when the listing offered both). */
  deliveryMethod?: DeliveryOption | null;
  /** The options the buyer can choose from now (empty when there's nothing to choose). */
  deliveryChoices: DeliveryOption[];
  /** Shipping is the method but the buyer hasn't added the address yet. */
  needsShippingAddress?: boolean;
  /** Shipping: where it goes. */
  shipTo?: Address | null;
  /** The same as one line (also older orders). */
  deliveryAddress?: string | null;
  /** Pickup: the seller's exact address (buyer, after the sale) or the area. */
  pickupAddress?: string | null;
  courier?: string | null;
  trackingNumber?: string | null;
  status: OrderStatus;
  /** The current step's name ("Out for delivery", "Picked up", …). */
  statusLabel: string;
  /** The tracking steps that apply to this delivery method, in order. */
  tracking: OrderStep[];
  /** What the signed-in user can do now (next step / confirm delivery). */
  actions: { status: OrderStatus; label: string }[];
  createdAt: string;
  handedOverAt?: string | null;
  completedAt?: string | null;
  contactEmail?: string | null;
  contactPhone?: string | null;
  /** The buyer's review of this order, if written. */
  reviewId?: string | null;
  /** The buyer can review it now ("How was your purchase?") – until reviewDeadline. */
  canReview?: boolean;
  reviewDeadline?: string | null;
}
