import { mapRawDtoToAuction } from '../src/services/api';
import {
  countryFromPhone,
  fallbackRadius,
  formatDistance,
  formatMoney,
  formatRadius,
  haversineKm,
  isCountryCode,
  MOVE_THRESHOLD_KM,
} from '../src/utils/geo';

describe('geo helpers', () => {
  test('Hyderabad to Bangalore is about 500 km (same as the backend GeoMath)', () => {
    expect(haversineKm(17.385, 78.4867, 12.9716, 77.5946)).toBeGreaterThan(490);
    expect(haversineKm(17.385, 78.4867, 12.9716, 77.5946)).toBeLessThan(510);
    expect(haversineKm(6.5244, 3.3792, 6.5244, 3.3792)).toBe(0);
  });

  test('the automatic location only moves after ~5 km', () => {
    expect(MOVE_THRESHOLD_KM).toBe(5);
  });

  test('country follows the phone code when the location is unknown (India by default)', () => {
    expect(countryFromPhone('+919876543210')).toBe('IN');
    expect(countryFromPhone('+12015550123')).toBe('US');
    expect(countryFromPhone('+2348031234567')).toBe('NG');
    expect(countryFromPhone('+353851234567')).toBe('IE');
    expect(countryFromPhone(null)).toBe('IN');
  });

  test('only India, USA, Nigeria and Ireland are supported countries', () => {
    expect(isCountryCode('us')).toBe(true);
    expect(isCountryCode('IE')).toBe(true);
    expect(isCountryCode('GB')).toBe(false);
    expect(isCountryCode(null)).toBe(false);
  });

  test('offline radius fallback: 60 miles in the USA, 60 km elsewhere', () => {
    expect(fallbackRadius('US')).toEqual({ radiusValue: 60, radiusUnit: 'mi', radiusKm: 96.56 });
    expect(fallbackRadius('IN')).toEqual({ radiusValue: 60, radiusUnit: 'km', radiusKm: 60 });
    expect(formatRadius(96.56, 'US')).toBe('60 mi');
    expect(formatRadius(60, 'NG')).toBe('60 km');
  });

  test('"12 km away", or miles in the USA', () => {
    expect(formatDistance(0)).toBe('< 1 km');
    expect(formatDistance(8.5)).toBe('8.5 km');
    expect(formatDistance(9.0)).toBe('9 km');
    expect(formatDistance(12.4)).toBe('12 km');
    expect(formatDistance(0.5, 'US')).toBe('< 1 mi');
    expect(formatDistance(16.09, 'US')).toBe('10 mi');
    expect(formatDistance(12.4, 'NG')).toBe('12 km');
  });

  test('currency follows the country', () => {
    expect(formatMoney(1500, 'US')).toBe('$1,500');
    expect(formatMoney(1500, 'IE')).toBe('€1,500');
    expect(formatMoney(1500, 'NG')).toBe('₦1,500');
    expect(formatMoney(1500, 'IN')).toBe('₹1,500');
    expect(formatMoney(45, 'IE', 2)).toBe('€45.00');
    expect(formatMoney(300, null)).toBe('₹300');
  });

  test('search results keep location, distance and category; no container or OKOH defaults', () => {
    const a = mapRawDtoToAuction({
      id: 'ford-gt', make: 'Ford', model: 'GT', year: 2020, seller: 'bob', partCategory: 'Body',
      locationName: 'Hyderabad', latitude: 17.385, longitude: 78.4867, distanceKm: 13.5,
    });
    expect(a.locationName).toBe('Hyderabad');
    expect(a.distanceKm).toBe(13.5);
    expect(a.item.partCategory).toBe('Body');
    expect(a.highBidder).toBe('');
    expect(a.pickupPort).toBeUndefined();
    expect(a.containerNumber).toBeUndefined();
    expect(a.bidCount).toBe(0);
    const old = mapRawDtoToAuction({ id: 'x', make: 'BMW', model: 'X1' });
    expect(old.distanceKm).toBeUndefined();
    expect(old.seller).not.toMatch(/okoh/i);
  });
});
