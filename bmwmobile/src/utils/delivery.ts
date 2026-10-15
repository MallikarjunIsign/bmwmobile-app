import type { DeliveryOption, Order, ShippingQuote } from '../types/listing';
import { countryName } from './geo';
import type { CountryCode } from '../types/location';

interface DeliveryInfo {
  deliveryOptions?: DeliveryOption[] | string[] | null;
  pickupCity?: string | null;
  /** Older listings without a pickup city. */
  locationName?: string | null;
}

/**
 * What the buyer can get, for cards and the listing: "📍 Pickup in Hyderabad (free) · 🚚 Shipping",
 * "📍 Pickup only · Hyderabad", "🚚 Shipping only". The shipping charge depends on the buyer's location, so it is
 * shown on the listing (estimate) and at checkout, not on the card. Null without delivery information.
 */
export function deliverySummary(info: DeliveryInfo, _money?: (n: number) => string): string | null {
  const options = (info.deliveryOptions ?? []) as string[];
  const pickup = options.includes('Pickup');
  const shipping = options.includes('Shipping');
  const city = info.pickupCity || info.locationName?.split(',').pop()?.trim() || null;

  if (pickup && shipping) return `📍 Pickup${city ? ` in ${city}` : ''} (free) · 🚚 Shipping`;
  if (pickup) return `📍 Pickup only${city ? ` · ${city}` : ''}`;
  if (shipping) return '🚚 Shipping only';
  return null;
}

/** "USPS Ground Advantage, zone 2" / "Freight, estimated rate" (US), or null elsewhere. */
export function shippingServiceText(service?: string | null, zone?: number | null, estimated = false): string | null {
  if (!service) return estimated ? 'estimated rate' : null;
  const text = zone ? `${service}, zone ${zone}` : service;
  return estimated ? `${text}, estimated rate` : text;
}

/** "Shipping to Kukatpally: ~₹350 (estimate)" / "Shipping to Philadelphia: ~$12 (USPS Ground Advantage, estimate)", or why not. */
export function shippingEstimateText(quote: ShippingQuote, place: string | null | undefined, money: (n: number) => string): string {
  if (!quote.available || quote.charge == null) return `Shipping not available${place ? ` to ${place}` : ''}: ${quote.reason ?? 'not offered'}`;
  // Freight / vehicle transport priced from industry averages say so: "(Freight, estimated rate)".
  if (quote.estimatedRate) return `Shipping${place ? ` to ${place}` : ''}: ~${money(quote.charge)} (${shippingServiceText(quote.service, null, true)})`;
  return `Shipping${place ? ` to ${place}` : ''}: ~${money(quote.charge)} (${quote.service ? `${quote.service}, ` : ''}estimate)`;
}

/** "₹350 · about 22 km", or "$12 · USPS Ground Advantage, zone 2 · 2 lb" (US) for the exact charge before confirming. */
export function shippingChargeText(quote: ShippingQuote, money: (n: number) => string): string {
  if (!quote.available || quote.charge == null) return quote.reason ?? 'Shipping not available';
  if (quote.service && quote.zone) {
    const weight = quote.billableWeightLb != null ? ` · ${quote.billableWeightLb} lb` : '';
    return `${money(quote.charge)} · ${shippingServiceText(quote.service, quote.zone)}${weight}`;
  }
  const service = shippingServiceText(quote.service, null, !!quote.estimatedRate);
  return `${money(quote.charge)}${service ? ` · ${service}` : ''}${quote.distance != null ? ` · about ${quote.distance.toLocaleString()} ${quote.unit}` : ''}`;
}

/** The order's delivery part of the breakdown: "shipping ₹350 (22 km)", "pickup free", or null while not chosen. */
export function orderShippingText(order: Order, money: (n: number) => string): string | null {
  if (order.deliveryMethod === 'Pickup') return 'pickup free';
  const charge = order.shippingCharge ?? order.shippingFee;
  if (order.deliveryMethod !== 'Shipping' || charge == null) return null;
  if (charge === 0) return 'free shipping';
  // US: "shipping $12 (USPS Ground Advantage, zone 2)"; elsewhere the distance.
  const service = shippingServiceText(order.shippingService, order.shippingZone, !!order.shippingRateEstimated);
  if (service) return `shipping ${money(charge)} (${service})`;
  const distance = order.shippingDistance != null ? ` (${order.shippingDistance.toLocaleString()} ${order.shippingDistanceUnit ?? 'km'})` : '';
  return `shipping ${money(charge)}${distance}`;
}

/** "Ships within India". */
export const shipsWithinText = (country?: string | null) =>
  country ? `Ships within ${countryName(country as CountryCode) ?? country}` : 'Ships within the seller’s country';
