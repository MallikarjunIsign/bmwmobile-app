export type ShippingStatus = 
  | 'Order Confirmed' 
  | 'Container Loaded' 
  | 'In Transit' 
  | 'Arrived at Port' 
  | 'Ready for Pickup' 
  | 'Delivered';

export type BuyerType = 'Wholesale' | 'Individual' | 'Workshop / Garage';

export type PickupPort = 
  | 'Lagos Apapa Port' 
  | 'Lagos Tin Can Island' 
  | 'Lagos Lekki Deep Sea Port' 
  | 'Port Harcourt Onne Port' 
  | 'Calabar Port';

export interface VehicleInfo {
  id: string;
  make: string;
  model: string;
  year: number;
  vin?: string;
  nickname?: string;
}

export interface UserProfile {
  id: string;
  fullName: string;
  email: string;
  phoneNumber: string;
  buyerType: BuyerType;
  preferredPort: PickupPort;
  garageVehicles: VehicleInfo[];
  pushNotifications: boolean;
  smsAlerts: boolean;
}

export interface ItemImage {
  id: string;
  url: string;
  isPrimary: boolean;
  sortOrder: number;
}

export interface Item {
  id: string;
  itemType: 'Vehicle' | 'Part';
  make: string;
  model: string;
  year: number;
  yearTo?: number;
  color?: string;
  mileage?: number;
  description: string;
  partName?: string;
  partNumber?: string;
  /** Body, Engine, Lighting, WheelsAndTyres, ... */
  partCategory?: string;
  condition?: string;
  containerId?: string;
  images: ItemImage[];
  displayName: string;
}

export interface Auction {
  id: string;
  title: string;
  item: Item;
  startingPrice: number;
  currentHighBid: number;
  highBidder?: string;
  bidCount: number;
  auctionEnd: string;
  status: 'Live' | 'EndingSoon' | 'Won' | 'Lost' | 'Ended' | 'Pending';
  seller: string;
  containerNumber?: string;
  estimatedArrival?: string;
  pickupPort?: string;
  isAutoBidActive?: boolean;
  userMaxAutoBid?: number;
  /** Where the item is ("Kukatpally, Hyderabad"). */
  locationName?: string;
  latitude?: number;
  longitude?: number;
  /** Distance from the selected location (only for "parts near" searches). */
  distanceKm?: number;
  /**
   * Inside the buyer's radius: pickup is offered to them. False: shown because it ships (pickup is too far).
   * Undefined without a location.
   */
  withinRadius?: boolean;
  /** The shipping estimate to the buyer's location (search results that ship; loaded after the results). */
  shippingQuote?: import('./listing').ShippingQuote | null;
  /** Listing model (search results). */
  kind?: 'CommonPart' | 'VehicleSpecificPart' | 'Vehicle';
  fitmentMode?: 'Universal' | 'SizeBased' | 'VehicleSpecific';
  partType?: string;
  /** "Fits Toyota Hilux 2016–2020", "Universal – fits any vehicle" … */
  fitmentSummary?: string;
  usedGrade?: string;
  /** "Fits your 2016 Toyota Hilux", "Universal", "Fits by size", "Check fitment". */
  badge?: string;
  thumbnailUrl?: string;
  currency?: string;
  countryCode?: string;
  /** Auction (bids until auctionEnd) or FixedPrice (buy now at startingPrice until sold out). */
  saleType?: SaleType;
  /** The seller's display name (older listings: undefined – show `seller`). */
  sellerName?: string;
  /** FixedPrice: units still for sale. */
  quantityAvailable?: number;
  /** Pickup and / or Shipping, the shipping fee (0 = free) and the public pickup city. */
  deliveryOptions?: ('Pickup' | 'Shipping')[];
  shippingFee?: number | null;
  pickupCity?: string | null;
  /** The seller's average rating (null: none yet) and number of ratings – "⭐ 4.7 (32)". */
  sellerRating?: number | null;
  sellerRatingCount?: number;
}

export type SaleType = 'Auction' | 'FixedPrice';

export interface Bid {
  id: string;
  auctionId: string;
  bidder: string;
  amount: number;
  bidTime: string;
  bidStatus?: string;
  isAutoBid?: boolean;
}

export interface Purchase {
  id: string;
  auctionId: string;
  buyer: string;
  itemTitle: string;
  winningBidAmount: number;
  buyerFee: number;
  shippingFee: number;
  totalAmount: number;
  deliveryOption: 'PortPickup' | 'DoorstepDelivery';
  deliveryAddress?: string;
  pickupPort: string;
  paymentMethod: 'BankTransfer' | 'Card';
  paymentStatus: 'Paid' | 'Pending';
  qrPassCode: string;
  shippingStatus: ShippingStatus;
  containerNumber?: string;
  imageUrl?: string;
  rating?: number;
  ratingComment?: string;
  createdAt: string;
}

/** Custom order status: Open until a matching listing is published (Matched) or the buyer closes it. */
export type CustomOrderStatus = 'Open' | 'Matched' | 'Closed';

/** A custom order ("PartRequest" on the server). */
export interface PartRequest {
  id: string;
  user: string;
  searchText?: string;
  /** Empty = any make / model; 0 = any year. */
  make: string;
  model: string;
  year: number;
  partName: string;
  partNumber?: string;
  vin?: string;
  specs?: string;
  maxBudget: number;
  locationName?: string;
  countryCode?: string;
  radiusKm?: number;
  preferredPort: string;
  status: CustomOrderStatus | string;
  /** The listing that matched this order. */
  matchedAuctionId?: string;
  matchedAt?: string;
  feasibilityNote?: string;
  estimatedDeliveryDate?: string;
  createdAt: string;
}

/** POST /partrequests */
export interface CreateCustomOrder {
  searchText?: string;
  partName?: string;
  partNumber?: string;
  make?: string;
  model?: string;
  year?: number;
  specs?: string;
  maxBudget?: number;
  locationName?: string;
  countryCode?: string;
  latitude?: number;
  longitude?: number;
  radiusKm?: number;
}

/**
 * GET /bids/{id}/next-minimum – the next valid bid and its calculation, worked out by BidService:
 * "Current bid $1,000 + step $20 = Next bid $1,020". Amounts are totals (part price + bid).
 */
export interface NextBid {
  minimumBid: number;
  currentTotal: number;
  bidStep: number;
  partPrice: number;
  currentHighBid: number | null;
  bidCount: number;
  highBidder: string | null;
  /** Next, next + 1 step, next + 2 steps, next + 5 steps. */
  quickBids: number[];
  auctionEnd: string;
  status: string;
}

/** The Marketplace pills (and later the filter sheet). */
export interface SearchFilters {
  partCategory?: string;
  make?: string;
  model?: string;
  /** Country-wide results when the location is unknown. */
  country?: string;
}

export interface FilterOptions {
  categories: { value: string; count: number }[];
  makes: { value: string; count: number }[];
  models: { make: string; model: string; count: number }[];
  totalCount: number;
}

export interface ContainerSchedule {
  id: string;
  code: string;
  originPort: string;
  originCountry: string;
  destinationPort: string;
  destinationCountry: string;
  cutoffDate: string;
  daysRemaining: number;
  departureDate: string;
  estimatedArrivalDate: string;
  capacityPercentage: number;
  status: 'Receiving Parts' | 'Scheduled' | 'Closed';
}
