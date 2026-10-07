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
}

export interface Bid {
  id: string;
  auctionId: string;
  bidder: string;
  amount: number;
  bidTime: string;
  isAutoBid: boolean;
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

export interface PartRequest {
  id: string;
  user: string;
  make: string;
  model: string;
  year: number;
  partName: string;
  vin?: string;
  specs?: string;
  maxBudget: number;
  preferredPort: string;
  status: 'Feasibility Checked' | 'Sourced' | 'Container Assigned' | 'In Transit' | 'Ready for Pickup';
  feasibilityNote?: string;
  estimatedDeliveryDate?: string;
  createdAt: string;
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
