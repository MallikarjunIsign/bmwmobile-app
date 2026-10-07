import axios, { AxiosError, InternalAxiosRequestConfig } from 'axios';
import { Auction, Bid, Purchase, PartRequest, ContainerSchedule, UserProfile } from '../types/models';
import { config } from '../config';
import { currentUsername, DEMO_OWNER, getValidAccessToken } from '../auth/session';

// Configured for local dev / GatewayService YARP proxy (http://localhost:6001 / http://10.0.2.2:6001)
const GATEWAY_URL = config.GATEWAY_URL;

const client = axios.create({
  baseURL: GATEWAY_URL,
  timeout: 5000,
  headers: {
    'Content-Type': 'application/json',
  },
});

// Signed-in users send their IdentityService access token; guests call the public endpoints only.
client.interceptors.request.use(async request => {
  const token = await getValidAccessToken().catch(() => null);
  if (token) request.headers.Authorization = `Bearer ${token}`;
  return request;
});

// Token rejected (expired / revoked): refresh once and retry; if that fails the user is signed out.
client.interceptors.response.use(undefined, async (error: AxiosError) => {
  const request = error.config as (InternalAxiosRequestConfig & { _retried?: boolean }) | undefined;
  if (error.response?.status === 401 && request?.headers?.Authorization && !request._retried) {
    request._retried = true;
    const token = await getValidAccessToken(true).catch(() => null);
    if (token) {
      request.headers.Authorization = `Bearer ${token}`;
      return client(request);
    }
  }
  return Promise.reject(error);
});

// Offline demo data: entries owned by "you" follow whoever is signed in (guests see them as "you").
let demoOwner = DEMO_OWNER;

export function setDemoOwner(username: string | null) {
  const next = username ?? DEMO_OWNER;
  MOCK_AUCTIONS.forEach(a => { if (a.highBidder === demoOwner) a.highBidder = next; });
  MOCK_PURCHASES.forEach(p => { if (p.buyer === demoOwner) p.buyer = next; });
  MOCK_PART_REQUESTS.forEach(r => { if (r.user === demoOwner) r.user = next; });
  demoOwner = next;
}

// Item location and, for "parts near" searches, the distance from the selected location.
function locationFields(raw: any): Pick<Auction, 'locationName' | 'latitude' | 'longitude' | 'distanceKm'> {
  return {
    locationName: raw.locationName ?? raw.LocationName ?? undefined,
    latitude: typeof raw.latitude === 'number' ? raw.latitude : undefined,
    longitude: typeof raw.longitude === 'number' ? raw.longitude : undefined,
    distanceKm: typeof raw.distanceKm === 'number' ? raw.distanceKm : undefined,
  };
}

/** The selected location the list is for: only parts within radiusKm, nearest first. */
export type NearFilter = { latitude: number; longitude: number; radiusKm: number };

// Utility to normalize raw backend DTO (flat AuctionDto or SearchItem) to full mobile Auction object
export function mapRawDtoToAuction(raw: any): Auction {
  if (!raw) return MOCK_AUCTIONS[0];

  // Nested format already
  if (raw.item && raw.item.make) {
    return {
      id: String(raw.id || raw.Id),
      title: raw.title || `${raw.item.year || 2021} ${raw.item.make} ${raw.item.model} ${raw.item.partName || ''}`.trim(),
      item: {
        id: String(raw.item.id || raw.id),
        itemType: raw.item.itemType || 'Part',
        make: raw.item.make,
        model: raw.item.model,
        year: raw.item.year || 2021,
        yearTo: raw.item.yearTo,
        color: raw.item.color,
        mileage: raw.item.mileage,
        description: raw.item.description || '',
        partName: raw.item.partName,
        partNumber: raw.item.partNumber,
        condition: raw.item.condition,
        images: Array.isArray(raw.item.images) && raw.item.images.length > 0
          ? raw.item.images.map((img: any, idx: number) =>
              typeof img === 'string'
                ? { id: `img-${idx}`, url: img, isPrimary: idx === 0, sortOrder: idx }
                : { id: String(img.id || `img-${idx}`), url: img.url || '', isPrimary: img.isPrimary ?? (idx === 0), sortOrder: img.sortOrder ?? idx })
          : [{ id: 'img-0', url: raw.imageUrl || raw.item.imageUrl || 'https://images.unsplash.com/photo-1617814076367-b759c7d7e738?q=80&w=800&auto=format&fit=crop', isPrimary: true, sortOrder: 0 }],
        displayName: raw.item.displayName || `${raw.item.year || 2021} ${raw.item.make} ${raw.item.model} ${raw.item.partName || ''}`.trim()
      },
      startingPrice: Number(raw.startingPrice ?? raw.StartingPrice ?? 100),
      currentHighBid: Number(raw.currentHighBid ?? raw.CurrentHighBid ?? raw.startingPrice ?? 100),
      highBidder: raw.highBidder || raw.winner || raw.Winner || '',
      bidCount: Number(raw.bidCount ?? 1),
      auctionEnd: raw.auctionEnd || raw.AuctionEnd || new Date(Date.now() + 86400000).toISOString(),
      status: raw.status || raw.Status || 'Live',
      seller: raw.seller || raw.Seller || 'OKOH_Seller',
      containerNumber: raw.containerNumber || raw.container?.containerNumber || raw.Container?.ContainerNumber || 'MSCU7821940',
      estimatedArrival: raw.estimatedArrival || raw.container?.estimatedArrivalDate || 'Nov 15, 2026',
      pickupPort: raw.pickupPort || raw.container?.destinationPort || 'Lagos Apapa Port',
      isAutoBidActive: Boolean(raw.isAutoBidActive),
      userMaxAutoBid: raw.userMaxAutoBid ? Number(raw.userMaxAutoBid) : undefined,
      ...locationFields(raw)
    };
  }

  // Flat DTO from .NET AuctionDto
  const make = raw.make || raw.Make || 'BMW';
  const model = raw.model || raw.Model || 'Parts';
  const year = raw.year || raw.Year || 2021;
  const partName = raw.partName || raw.PartName || '';
  const title = raw.title || `${year} ${make} ${model} ${partName}`.trim();
  const rawImages = raw.images || raw.Images || [];
  const primaryUrl = raw.imageUrl || raw.ImageUrl || (rawImages.length > 0 ? (typeof rawImages[0] === 'string' ? rawImages[0] : rawImages[0].url) : 'https://images.unsplash.com/photo-1617814076367-b759c7d7e738?q=80&w=800&auto=format&fit=crop');

  const formattedImages = Array.isArray(rawImages) && rawImages.length > 0
    ? rawImages.map((img: any, idx: number) => 
        typeof img === 'string' 
          ? { id: `img-${idx}`, url: img, isPrimary: idx === 0, sortOrder: idx }
          : { id: String(img.id || `img-${idx}`), url: img.url || primaryUrl, isPrimary: img.isPrimary ?? (idx === 0), sortOrder: img.sortOrder ?? idx })
    : [{ id: 'img-0', url: primaryUrl, isPrimary: true, sortOrder: 0 }];

  return {
    id: String(raw.id || raw.Id),
    title,
    item: {
      id: String(raw.id || raw.Id),
      itemType: raw.itemType || raw.ItemType || 'Part',
      make,
      model,
      year,
      yearTo: raw.yearTo || raw.YearTo,
      color: raw.color || raw.Color,
      mileage: raw.mileage || raw.Mileage,
      description: raw.description || raw.Description || '',
      partName,
      partNumber: raw.partNumber || raw.PartNumber,
      condition: raw.condition || raw.Condition,
      images: formattedImages,
      displayName: title
    },
    startingPrice: Number(raw.startingPrice ?? raw.StartingPrice ?? 100),
    currentHighBid: Number(raw.currentHighBid ?? raw.CurrentHighBid ?? raw.startingPrice ?? 100),
    highBidder: raw.highBidder || raw.winner || raw.Winner || '',
    bidCount: Number(raw.bidCount ?? 1),
    auctionEnd: raw.auctionEnd || raw.AuctionEnd || new Date(Date.now() + 86400000).toISOString(),
    status: raw.status || raw.Status || 'Live',
    seller: raw.seller || raw.Seller || 'OKOH_Seller',
    containerNumber: raw.containerNumber || raw.container?.containerNumber || raw.Container?.ContainerNumber || 'MSCU7821940',
    estimatedArrival: raw.estimatedArrival || raw.container?.estimatedArrivalDate || 'Nov 15, 2026',
    pickupPort: raw.pickupPort || raw.container?.destinationPort || 'Lagos Apapa Port',
    isAutoBidActive: Boolean(raw.isAutoBidActive),
    userMaxAutoBid: raw.userMaxAutoBid ? Number(raw.userMaxAutoBid) : undefined,
      ...locationFields(raw)
  };
}

// Seed Data
export const MOCK_AUCTIONS: Auction[] = [
  {
    id: 'auc-bmw-m4-01',
    title: '2021 BMW M4 Competition S58 Twin-Turbo Engine Assembly',
    item: {
      id: 'itm-01',
      itemType: 'Part',
      make: 'BMW',
      model: 'M4 Competition',
      year: 2021,
      displayName: '2021 BMW M4 Competition S58 Twin-Turbo Engine Assembly',
      description: 'Complete S58 twin-turbo 3.0L inline-6 engine unit with wiring harness, turbos, and ECU. Tested with 18,400 km.',
      partName: 'Engine Assembly',
      partNumber: '11002472910',
      condition: 'Excellent / Grade A',
      images: [
        { id: 'img-1', url: 'https://images.unsplash.com/photo-1617814076367-b759c7d7e738?q=80&w=800&auto=format&fit=crop', isPrimary: true, sortOrder: 0 },
        { id: 'img-2', url: 'https://images.unsplash.com/photo-1503376780353-7e6692767b70?q=80&w=800&auto=format&fit=crop', isPrimary: false, sortOrder: 1 }
      ]
    },
    startingPrice: 5000,
    currentHighBid: 14500,
    highBidder: DEMO_OWNER,
    bidCount: 28,
    auctionEnd: new Date(Date.now() + 1000 * 60 * 14).toISOString(),
    status: 'EndingSoon',
    seller: 'BavariaParts_DE',
    containerNumber: 'MSCU7821940',
    estimatedArrival: 'Nov 02, 2026',
    pickupPort: 'Lagos Apapa Port',
    isAutoBidActive: true,
    userMaxAutoBid: 16000
  },
  {
    id: 'auc-bmw-330-02',
    title: '2019 BMW 330i M-Sport LaserLight Headlight Set (L+R)',
    item: {
      id: 'itm-02',
      itemType: 'Part',
      make: 'BMW',
      model: '3 Series 330i',
      year: 2019,
      displayName: '2019 BMW 330i M-Sport LaserLight Headlight Set',
      description: 'OEM BMW LaserLight adaptive headlights for G20 3-Series. Original modules and control units included.',
      partName: 'LaserLight Headlights',
      partNumber: '63118496155',
      condition: 'Like New',
      images: [
        { id: 'img-3', url: 'https://images.unsplash.com/photo-1552519507-da3b142c6e3d?q=80&w=800&auto=format&fit=crop', isPrimary: true, sortOrder: 0 }
      ]
    },
    startingPrice: 800,
    currentHighBid: 2450,
    highBidder: 'autoworks_lagos',
    bidCount: 14,
    auctionEnd: new Date(Date.now() + 1000 * 60 * 60 * 5).toISOString(),
    status: 'Live',
    seller: 'Munich_Auto_Exports',
    containerNumber: 'CN-2026-09',
    estimatedArrival: 'Nov 15, 2026',
    pickupPort: 'Lagos Tin Can Island'
  },
  {
    id: 'auc-bmw-x5-03',
    title: '2022 BMW X5 xDrive40i M-Sport Front Bumper & Grille Assembly',
    item: {
      id: 'itm-03',
      itemType: 'Part',
      make: 'BMW',
      model: 'X5 xDrive40i',
      year: 2022,
      displayName: '2022 BMW X5 Front Bumper & Iconic Glow Grille',
      description: 'Complete front fascia kit in Black Sapphire Metallic with PDC sensor cutouts and camera hole.',
      partName: 'Front Bumper Assembly',
      partNumber: '51118099201',
      condition: 'Grade A Refurbished',
      images: [
        { id: 'img-4', url: 'https://images.unsplash.com/photo-1580273916550-e323be2ae537?q=80&w=800&auto=format&fit=crop', isPrimary: true, sortOrder: 0 }
      ]
    },
    startingPrice: 1200,
    currentHighBid: 3100,
    highBidder: DEMO_OWNER,
    bidCount: 19,
    auctionEnd: new Date(Date.now() + 1000 * 60 * 60 * 24).toISOString(),
    status: 'Live',
    seller: 'BavariaParts_DE',
    containerNumber: 'MSCU7821940',
    estimatedArrival: 'Nov 02, 2026',
    pickupPort: 'Lagos Apapa Port'
  }
];

export const MOCK_PURCHASES: Purchase[] = [
  {
    id: 'pur-101',
    auctionId: 'auc-bmw-m4-01',
    buyer: DEMO_OWNER,
    itemTitle: '2021 BMW M4 Competition S58 Twin-Turbo Engine Assembly',
    winningBidAmount: 14500,
    buyerFee: 435,
    shippingFee: 0,
    totalAmount: 14935,
    deliveryOption: 'PortPickup',
    pickupPort: 'Lagos Apapa Port',
    paymentMethod: 'BankTransfer',
    paymentStatus: 'Paid',
    qrPassCode: 'PU-1042-7731',
    shippingStatus: 'Ready for Pickup',
    containerNumber: 'MSCU7821940',
    imageUrl: 'https://images.unsplash.com/photo-1617814076367-b759c7d7e738?q=80&w=800&auto=format&fit=crop',
    createdAt: new Date(Date.now() - 1000 * 60 * 60 * 24 * 5).toISOString()
  }
];

export const MOCK_PART_REQUESTS: PartRequest[] = [
  {
    id: 'pr-801',
    user: DEMO_OWNER,
    make: 'BMW',
    model: 'M3 G80',
    year: 2022,
    partName: 'Carbon Ceramic Brake Kit (Front + Rear)',
    specs: '6-piston front calipers in Gold with 400mm rotors',
    maxBudget: 6500,
    preferredPort: 'Lagos Apapa Port',
    status: 'Feasibility Checked',
    feasibilityNote: 'Matched with Container CN-2026-09. Cutoff in 12 days. Destination: Lagos Apapa Port.',
    estimatedDeliveryDate: '2026-11-20',
    createdAt: new Date(Date.now() - 1000 * 60 * 60 * 24 * 2).toISOString()
  }
];

export const MOCK_CONTAINER_SCHEDULES: ContainerSchedule[] = [
  {
    id: 'cont-01',
    code: 'CN-2026-09',
    originPort: 'Hamburg Port, Germany',
    originCountry: 'Germany',
    destinationPort: 'Lagos Apapa Port',
    destinationCountry: 'Nigeria',
    cutoffDate: 'Oct 15, 2026',
    daysRemaining: 12,
    departureDate: 'Oct 20, 2026',
    estimatedArrivalDate: 'Nov 08, 2026',
    capacityPercentage: 78,
    status: 'Receiving Parts'
  },
  {
    id: 'cont-02',
    code: 'CN-2026-10',
    originPort: 'Rotterdam Port, Netherlands',
    originCountry: 'Netherlands',
    destinationPort: 'Lagos Tin Can Island',
    destinationCountry: 'Nigeria',
    cutoffDate: 'Oct 28, 2026',
    daysRemaining: 25,
    departureDate: 'Nov 02, 2026',
    estimatedArrivalDate: 'Nov 22, 2026',
    capacityPercentage: 42,
    status: 'Scheduled'
  },
  {
    id: 'cont-03',
    code: 'CN-2026-11',
    originPort: 'Antwerp Port, Belgium',
    originCountry: 'Belgium',
    destinationPort: 'Port Harcourt Onne Port',
    destinationCountry: 'Nigeria',
    cutoffDate: 'Nov 10, 2026',
    daysRemaining: 38,
    departureDate: 'Nov 15, 2026',
    estimatedArrivalDate: 'Dec 05, 2026',
    capacityPercentage: 15,
    status: 'Scheduled'
  }
];

export const MOCK_USER_PROFILE: UserProfile = {
  id: 'usr-01',
  fullName: 'Tunde Afolabi',
  email: 'tunde@okohparts.ng',
  phoneNumber: '+234 803 123 4567',
  buyerType: 'Workshop / Garage',
  preferredPort: 'Lagos Apapa Port',
  garageVehicles: [
    { id: 'v1', make: 'BMW', model: '330i xDrive', year: 2018, nickname: 'Daily 330i' },
    { id: 'v2', make: 'BMW', model: 'M4 Competition', year: 2021, nickname: 'Track M4' }
  ],
  pushNotifications: true,
  smsAlerts: true
};

// API Services mapping live endpoints with DTO normalization & fallback
export const ApiService = {
  getAuctions: async (searchTerm?: string, near?: NearFilter): Promise<Auction[]> => {
    try {
      if (near) {
        // SearchService: bounding box + exact distance, each result with distanceKm.
        const res = await client.get('/search', {
          params: {
            searchTerm: searchTerm || undefined,
            lat: near.latitude,
            lon: near.longitude,
            radiusKm: near.radiusKm,
            orderBy: 'distance',
            pageSize: 50,
          },
        });
        if (res.data && Array.isArray(res.data.results)) {
          return res.data.results.map(mapRawDtoToAuction);
        }
      } else if (searchTerm) {
        const res = await client.get('/search', { params: { searchTerm } });
        if (res.data && Array.isArray(res.data.results)) {
          return res.data.results.map(mapRawDtoToAuction);
        }
      } else {
        const res = await client.get('/auctions');
        if (res.data && Array.isArray(res.data)) {
          return res.data.map(mapRawDtoToAuction);
        }
      }
    } catch (e) {
      // Fallback
    }

    if (searchTerm) {
      const term = searchTerm.toLowerCase();
      return MOCK_AUCTIONS.filter(a => 
        a.title.toLowerCase().includes(term) || 
        a.item.make.toLowerCase().includes(term) || 
        a.item.model.toLowerCase().includes(term)
      );
    }
    return MOCK_AUCTIONS;
  },

  getAuctionById: async (id: string): Promise<Auction | undefined> => {
    try {
      const res = await client.get(`/auctions/${id}`);
      if (res.data) return mapRawDtoToAuction(res.data);
    } catch (e) {
      // Fallback
    }
    return MOCK_AUCTIONS.find(a => a.id === id) || MOCK_AUCTIONS[0];
  },

  placeBid: async (auctionId: string, amount: number): Promise<Bid> => {
    try {
      const res = await client.post('/bids', { auctionId, amount });
      if (res.data) return res.data;
    } catch (e) {
      // Fallback
    }
    const auction = MOCK_AUCTIONS.find(a => a.id === auctionId);
    if (auction) {
      auction.currentHighBid = amount;
      auction.highBidder = currentUsername();
      auction.bidCount += 1;
    }
    return {
      id: 'bid-' + Date.now(),
      auctionId,
      bidder: currentUsername(),
      amount,
      bidTime: new Date().toISOString(),
      isAutoBid: false
    };
  },

  setAutoBid: async (auctionId: string, maxAmount: number): Promise<void> => {
    const auction = MOCK_AUCTIONS.find(a => a.id === auctionId);
    if (auction) {
      auction.isAutoBidActive = true;
      auction.userMaxAutoBid = maxAmount;
    }
  },

  checkout: async (params: {
    auctionId: string;
    deliveryOption: 'PortPickup' | 'DoorstepDelivery';
    deliveryAddress?: string;
    pickupPort: string;
    paymentMethod: 'BankTransfer' | 'Card';
  }): Promise<Purchase> => {
    try {
      const res = await client.post('/purchases/checkout', params);
      if (res.data) return res.data;
    } catch (e) {
      // Fallback
    }
    const auction = MOCK_AUCTIONS.find(a => a.id === params.auctionId) || MOCK_AUCTIONS[0];
    const newPurchase: Purchase = {
      id: 'pur-' + Date.now(),
      auctionId: params.auctionId,
      buyer: currentUsername(),
      itemTitle: auction.title,
      winningBidAmount: auction.currentHighBid,
      buyerFee: auction.currentHighBid * 0.03,
      shippingFee: params.deliveryOption === 'DoorstepDelivery' ? 45 : 0,
      totalAmount: auction.currentHighBid * 1.03 + (params.deliveryOption === 'DoorstepDelivery' ? 45 : 0),
      deliveryOption: params.deliveryOption,
      deliveryAddress: params.deliveryAddress,
      pickupPort: params.pickupPort,
      paymentMethod: params.paymentMethod,
      paymentStatus: 'Paid',
      qrPassCode: `PU-${Math.floor(1000 + Math.random() * 9000)}-${Math.floor(1000 + Math.random() * 9000)}`,
      shippingStatus: 'Order Confirmed',
      containerNumber: 'CN-2026-09',
      imageUrl: auction.item.images[0]?.url,
      createdAt: new Date().toISOString()
    };
    MOCK_PURCHASES.unshift(newPurchase);
    return newPurchase;
  },

  getPurchases: async (): Promise<Purchase[]> => {
    try {
      const res = await client.get('/purchases');
      if (res.data && Array.isArray(res.data)) return res.data;
    } catch (e) {
      // Fallback
    }
    return MOCK_PURCHASES;
  },

  submitRating: async (purchaseId: string, rating: number, comment?: string): Promise<void> => {
    try {
      await client.post(`/purchases/${purchaseId}/rating`, { rating, comment });
    } catch (e) {
      const purchase = MOCK_PURCHASES.find(p => p.id === purchaseId);
      if (purchase) {
        purchase.rating = rating;
        purchase.ratingComment = comment;
      }
    }
  },

  getPlannedContainers: async (): Promise<ContainerSchedule[]> => {
    try {
      const res = await client.get('/containers/planned');
      if (res.data && Array.isArray(res.data)) return res.data;
    } catch (e) {
      // Fallback
    }
    return MOCK_CONTAINER_SCHEDULES;
  },

  createPartRequest: async (dto: {
    make: string;
    model: string;
    year: number;
    partName: string;
    vin?: string;
    specs?: string;
    maxBudget: number;
    preferredPort: string;
  }): Promise<PartRequest> => {
    try {
      const res = await client.post('/partrequests', dto);
      if (res.data) return res.data;
    } catch (e) {
      // Fallback
    }
    const newReq: PartRequest = {
      id: 'pr-' + Date.now(),
      user: currentUsername(),
      make: dto.make,
      model: dto.model,
      year: dto.year,
      partName: dto.partName,
      vin: dto.vin,
      specs: dto.specs,
      maxBudget: dto.maxBudget,
      preferredPort: dto.preferredPort,
      status: 'Feasibility Checked',
      feasibilityNote: `Matched with Container CN-2026-09. Cutoff in 12 days. Destination: ${dto.preferredPort}`,
      estimatedDeliveryDate: new Date(Date.now() + 1000 * 60 * 60 * 24 * 35).toISOString(),
      createdAt: new Date().toISOString()
    };
    MOCK_PART_REQUESTS.unshift(newReq);
    return newReq;
  },

  getPartRequests: async (): Promise<PartRequest[]> => {
    try {
      const res = await client.get('/partrequests');
      if (res.data && Array.isArray(res.data)) return res.data;
    } catch (e) {
      // Fallback
    }
    return MOCK_PART_REQUESTS;
  }
};
