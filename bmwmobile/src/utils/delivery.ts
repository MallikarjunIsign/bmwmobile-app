import type { DeliveryOption } from '../types/listing';
import { countryName } from './geo';
import type { CountryCode } from '../types/location';

interface DeliveryInfo {
  deliveryOptions?: DeliveryOption[] | string[] | null;
  shippingFee?: number | null;
  pickupCity?: string | null;
  /** Older listings without a pickup city. */
  locationName?: string | null;
}

/** "₹500", "Free shipping" or "" while the fee isn't known (older listings). */
export function shippingFeeText(fee: number | null | undefined, money: (n: number) => string): string {
  if (fee === 0) return 'Free shipping';
  return typeof fee === 'number' ? money(fee) : '';
}

/**
 * What the buyer can get, for cards and the listing detail:
 * "📍 Pickup in Hyderabad · 🚚 Shipping ₹500", "📍 Pickup only · Hyderabad", "🚚 Shipping only · Free shipping".
 * Null when the listing has no delivery information.
 */
export function deliverySummary(info: DeliveryInfo, money: (n: number) => string): string | null {
  const options = (info.deliveryOptions ?? []) as string[];
  const pickup = options.includes('Pickup');
  const shipping = options.includes('Shipping');
  const city = info.pickupCity || info.locationName?.split(',').pop()?.trim() || null;
  const fee = shippingFeeText(info.shippingFee, money);

  if (pickup && shipping) {
    const shippingText = fee === 'Free shipping' ? '🚚 Free shipping' : `🚚 Shipping${fee ? ` ${fee}` : ''}`;
    return `📍 Pickup${city ? ` in ${city}` : ''} · ${shippingText}`;
  }
  if (pickup) return `📍 Pickup only${city ? ` · ${city}` : ''}`;
  if (shipping) return `🚚 Shipping only${fee ? ` · ${fee}` : ''}`;
  return null;
}

/** "Ships within India". */
export const shipsWithinText = (country?: string | null) =>
  country ? `Ships within ${countryName(country as CountryCode) ?? country}` : 'Ships within the seller’s country';
