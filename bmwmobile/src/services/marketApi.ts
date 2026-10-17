import { fixUrl, http, mapRawDtoToAuction, SLOW_REQUEST, toServerError, type NearFilter } from './api';
import type { Auction, Bid } from '../types/models';
import type {
  Address,
  Category,
  DeliveryOption,
  Fitment,
  GarageVehicle,
  ItemTypeFilter,
  OpenPartRequest,
  ShippingEstimate,
  Listing,
  ListingImage,
  ListingKind,
  ListingSearchQuery,
  ListingSearchResult,
  Make,
  MyBid,
  MyListing,
  Order,
  OrderStatus,
  PartType,
  SaveListing,
  ShippingAddress,
  Suggestion,
  VehicleModel,
} from '../types/listing';

/** Every call throws ServerError (message safe to show, `fields` for validation problems). */
async function call<T>(request: () => Promise<{ data: T }>, fallback: string): Promise<T> {
  try {
    return (await request()).data;
  } catch (e) {
    throw toServerError(e, fallback);
  }
}

const fixListing = (l: Listing): Listing => ({
  ...l,
  imageUrl: fixUrl(l.imageUrl) ?? '',
  thumbnailUrl: fixUrl(l.thumbnailUrl),
  imageItems: (l.imageItems ?? []).map(i => ({ ...i, url: fixUrl(i.url) ?? i.url, thumbnailUrl: fixUrl(i.thumbnailUrl) })),
});

/** A photo picked on the phone (already resized / compressed by the picker). */
export interface LocalPhoto {
  uri: string;
  type?: string;
  fileName?: string;
}

/** "+ Sell", master data, search, garage, bids and orders (all through the Gateway). */
export const MarketApi = {
  // ── catalogue ──
  /** commonOnly: only part types that fit any vehicle, and the categories that contain them. */
  categories: (commonOnly?: boolean) =>
    call<Category[]>(() => http.get('/catalog/categories', { params: { commonOnly: commonOnly || undefined } }), "Couldn't load the part catalogue"),

  partTypes: (q: string, options?: { fitmentMode?: string; commonOnly?: boolean }) =>
    call<PartType[]>(
      () => http.get('/catalog/part-types', { params: { q, fitmentMode: options?.fitmentMode, commonOnly: options?.commonOnly || undefined, limit: 20 } }),
      "Couldn't load part types",
    ),

  partType: (id: number) => call<PartType>(() => http.get(`/catalog/part-types/${id}`), "Couldn't load the part type"),

  /** The bid step table from BidService (shown read-only to sellers). */
  bidSteps: () =>
    call<{ from: number; to: number | null; step: number }[]>(() => http.get('/bids/steps'), "Couldn't load the bid steps"),

  /** Public listing detail (AuctionService). */
  listingDetail: (id: string) => call<Listing>(() => http.get(`/auctions/${id}`), "Couldn't load the listing").then(fixListing),

  makes: (q?: string, country?: string) =>
    call<Make[]>(() => http.get('/catalog/makes', { params: { q: q || undefined, country } }), "Couldn't load makes"),

  models: (makeId: number, q?: string, country?: string) =>
    call<VehicleModel[]>(() => http.get(`/catalog/makes/${makeId}/models`, { params: { q: q || undefined, country } }), "Couldn't load models"),

  /** "Other – request to add": usable on your listing right away, approved by an admin later. */
  requestMake: (name: string) => call<Make>(() => http.post('/catalog/makes/request', { name }), "Couldn't add the make"),

  requestModel: (makeId: number, name: string) =>
    call<VehicleModel>(() => http.post('/catalog/models/request', { makeId, name }), "Couldn't add the model"),

  // ── selling ──
  createListing: (kind: ListingKind, extra: { partTypeId?: number; countryCode?: string; locationName?: string; latitude?: number; longitude?: number }) =>
    call<Listing>(() => http.post('/listings', { kind, ...extra }), "Couldn't start the listing").then(fixListing),

  getListing: (id: string) => call<Listing>(() => http.get(`/listings/${id}`), "Couldn't load the listing").then(fixListing),

  saveListing: (id: string, body: SaveListing) =>
    call<Listing>(() => http.put(`/listings/${id}`, body), "Couldn't save the listing").then(fixListing),

  publishListing: (id: string) =>
    call<Listing>(() => http.post(`/listings/${id}/publish`), "Couldn't publish the listing").then(fixListing),

  cancelListing: (id: string) => call<unknown>(() => http.post(`/listings/${id}/cancel`), "Couldn't cancel the listing"),

  addNote: (id: string, text: string) =>
    call<Listing>(() => http.post(`/listings/${id}/notes`, { text }), "Couldn't add the note").then(fixListing),

  relist: (id: string) => call<Listing>(() => http.post(`/listings/${id}/relist`), "Couldn't relist").then(fixListing),

  /** Buy now: units of a fixed-price listing. Returns the new order (409 when someone else just bought the last ones). */
  buy: (id: string, quantity: number) =>
    call<Order>(() => http.post(`/listings/${id}/buy`, { quantity }), "Couldn't complete the purchase").then(o => ({
      ...o,
      imageUrl: fixUrl(o.imageUrl),
    })),

  myListings: () =>
    call<MyListing[]>(() => http.get('/listings/mine'), "Couldn't load your listings").then(list =>
      list.map(l => ({ ...l, imageUrl: fixUrl(l.imageUrl) }))),

  lastFitment: (exceptId?: string) =>
    call<Fitment[]>(() => http.get('/listings/last-fitment', { params: { exceptId } }), "Couldn't load your last fitment"),

  /** Uploads photos (multipart) with progress 0..1. Vehicles pass the guided angle. */
  uploadPhotos: (id: string, photos: LocalPhoto[], onProgress?: (fraction: number) => void, angle?: string) => {
    const form = new FormData();
    photos.forEach((p, i) =>
      form.append('files', { uri: p.uri, type: p.type ?? 'image/jpeg', name: p.fileName ?? `photo-${i}.jpg` } as any),
    );
    if (angle) form.append('angle', angle);
    return call<ListingImage[]>(
      () =>
        http.post(`/auctions/${id}/images`, form, {
          headers: { 'Content-Type': 'multipart/form-data' },
          timeout: 120000,
          onUploadProgress: e => onProgress?.(e.total ? e.loaded / e.total : 0),
          transformRequest: data => data,
        }),
      "Couldn't upload the photos",
    ).then(list => list.map(i => ({ ...i, url: fixUrl(i.url) ?? i.url, thumbnailUrl: fixUrl(i.thumbnailUrl) })));
  },

  deletePhoto: (id: string, imageId: string) =>
    call<unknown>(() => http.delete(`/auctions/${id}/images/${imageId}`), "Couldn't delete the photo"),

  /** New order; the first photo is the cover. */
  reorderPhotos: (id: string, imageIds: string[]) =>
    call<ListingImage[]>(() => http.put(`/auctions/${id}/images/order`, { imageIds, primaryImageId: imageIds[0] }), "Couldn't reorder the photos")
      .then(list => list.map(i => ({ ...i, url: fixUrl(i.url) ?? i.url, thumbnailUrl: fixUrl(i.thumbnailUrl) }))),

  // ── search ──
  /**
   * Live listings the buyer can get: pickup-only ones inside `near`'s radius, ones that ship anywhere in `country`
   * (never another country). Without `near`: the whole country. The rule is applied by the Search service.
   */
  search: async (query: ListingSearchQuery, near?: NearFilter, country?: string): Promise<ListingSearchResult> => {
    const result = await call<ListingSearchResult>(
      () =>
        http.get('/search/listings', {
          params: {
            ...query,
            ...(near ? { lat: near.latitude, lon: near.longitude, radiusKm: near.radiusKm } : {}),
            country,
            pageSize: query.pageSize ?? 40,
          },
        }),
      "Couldn't search right now",
    );
    return {
      ...result,
      results: result.results.map(r => ({ ...r, imageUrl: fixUrl(r.imageUrl) ?? '', thumbnailUrl: fixUrl(r.thumbnailUrl) })),
    };
  },

  /** Suggestions while typing, limited to the Parts / Vehicles choice and the listings the buyer can see (like search). */
  suggest: async (q: string, country?: string, itemType?: ItemTypeFilter, near?: NearFilter): Promise<Suggestion[]> => {
    try {
      const res = await http.get('/search/suggest', {
        params: { q, country, itemType, ...(near ? { lat: near.latitude, lon: near.longitude, radiusKm: near.radiusKm } : {}) },
      });
      return (res.data?.suggestions ?? []) as Suggestion[];
    } catch {
      return [];
    }
  },

  /** A search hit as the Auction used by the cards and the bid screens. */
  hitToAuction: (hit: ListingSearchResult['results'][number]): Auction => mapRawDtoToAuction(hit),

  // ── garage ──
  garage: () => call<GarageVehicle[]>(() => http.get('/garage'), "Couldn't load your garage"),
  addGarageVehicle: (v: GarageVehicle) => call<GarageVehicle>(() => http.post('/garage', v), "Couldn't save the vehicle"),
  deleteGarageVehicle: (id: string) => call<unknown>(() => http.delete(`/garage/${id}`), "Couldn't remove the vehicle"),

  // ── bids ──
  myBids: () => call<MyBid[]>(() => http.get('/bids/mine'), "Couldn't load your bids"),
  bidHistory: (auctionId: string) => call<Bid[]>(() => http.get(`/bids/${auctionId}`), "Couldn't load the bid history"),
  /** The seller accepts the current highest bid and closes the auction. */
  acceptBid: (auctionId: string, bidId: string) =>
    call<Bid>(() => http.post('/bids/accept', { auctionId, bidId }), "Couldn't accept the bid"),

  // ── orders ──
  order: (id: string) =>
    call<Order>(() => http.get(`/orders/${id}`), "Couldn't load the order").then(o => ({ ...o, imageUrl: fixUrl(o.imageUrl) })),
  orders: () =>
    call<Order[]>(() => http.get('/orders'), "Couldn't load your orders").then(list =>
      list.map(o => ({ ...o, imageUrl: fixUrl(o.imageUrl) }))),
  /** Move the order to a tracking step (seller: ReadyForPickup, PickedUp, Shipped; buyer: Delivered / Completed). */
  orderStep: (id: string, status: OrderStatus, details?: { courier?: string; trackingNumber?: string }) =>
    call<Order>(() => http.post(`/orders/${id}/status`, { status, ...details }), "Couldn't update the order"),
  /**
   * The buyer chooses Pickup or Shipping, or adds the shipping address. Shipping goes to an address-book entry
   * (addressId) or a new address, which is saved to the address book.
   */
  /**
   * Pickup or Shipping. Shipping sends the address and the delivery location (latitude / longitude); the backend
   * calculates the charge and fixes it on the order.
   */
  chooseDelivery: (id: string, method: DeliveryOption, to?: { addressId?: string; address?: Address; latitude?: number; longitude?: number }) =>
    call<Order>(() => http.post(`/orders/${id}/delivery-method`, { method, ...to }, SLOW_REQUEST), "Couldn't save the delivery choice"),

  /** Sellers: open part requests from buyers, newest first (no buyer names, only their city). */
  openPartRequests: (country?: string) =>
    call<OpenPartRequest[]>(() => http.get('/partrequests/open', { params: { country } }), "Couldn't load buyer requests"),

  /** The shipping charge for a listing to a location (estimate on the listing; exact before confirming an order). */
  shippingEstimate: (listingId: string, latitude: number, longitude: number, country?: string | null) =>
    call<ShippingEstimate>(
      () => http.get('/shipping/estimate', { ...SLOW_REQUEST, params: { listingId, lat: latitude, lon: longitude, country: country ?? undefined } }),
      "Couldn't work out the shipping charge",
    ),

  /** Estimates for a page of results (the cards' "🚚 Ships to you ~$12"), in one call. At most 50 listings. */
  shippingEstimates: (listingIds: string[], latitude: number, longitude: number, country?: string | null) =>
    call<ShippingEstimate[]>(
      () => http.post('/shipping/estimates', { listingIds: listingIds.slice(0, 50), lat: latitude, lon: longitude, country: country ?? undefined }),
      "Couldn't work out the shipping charges",
    ),

  // ── shipping address book ──
  shippingAddresses: () => call<ShippingAddress[]>(() => http.get('/shipping-addresses', SLOW_REQUEST), "Couldn't load your addresses"),
  addShippingAddress: (a: Address & { isDefault?: boolean }) =>
    call<ShippingAddress>(() => http.post('/shipping-addresses', a, SLOW_REQUEST), "Couldn't save the address"),
  updateShippingAddress: (id: string, a: Address & { isDefault?: boolean }) =>
    call<ShippingAddress>(() => http.put(`/shipping-addresses/${id}`, a), "Couldn't save the address"),
  setDefaultShippingAddress: (id: string) =>
    call<ShippingAddress>(() => http.post(`/shipping-addresses/${id}/default`), "Couldn't change the default address"),
  deleteShippingAddress: (id: string) => call<unknown>(() => http.delete(`/shipping-addresses/${id}`), "Couldn't delete the address"),
};
