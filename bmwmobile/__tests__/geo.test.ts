import { mapRawDtoToAuction } from '../src/services/api';
import {
  countryFromPhone,
  DEFAULT_RADIUS_KM,
  formatDistance,
  formatMoney,
  formatRadius,
  nearestRadiusKm,
  radiusOptionsKm,
  noPartsNearMessage,
  haversineKm,
  locationChipLabel,
  locationIcon,
  RADIUS_OPTIONS_KM,
  radiusSummary,
} from '../src/utils/geo';

describe('geo helpers', () => {
  test('Hyderabad to Bangalore is about 500 km (same as the backend GeoMath)', () => {
    expect(haversineKm(17.385, 78.4867, 12.9716, 77.5946)).toBeGreaterThan(490);
    expect(haversineKm(17.385, 78.4867, 12.9716, 77.5946)).toBeLessThan(510);
    expect(haversineKm(6.5244, 3.3792, 6.5244, 3.3792)).toBe(0);
  });

  test('radius chips 5/10/25/50/100/200 with 50 as default', () => {
    expect([...RADIUS_OPTIONS_KM]).toEqual([5, 10, 25, 50, 100, 200]);
    expect(DEFAULT_RADIUS_KM).toBe(50);
  });

  test('popular-city country follows the phone code (India by default)', () => {
    expect(countryFromPhone('+919876543210')).toBe('IN');
    expect(countryFromPhone('+12015550123')).toBe('US');
    expect(countryFromPhone('+2348031234567')).toBe('NG');
    expect(countryFromPhone('+353851234567')).toBe('IE');
    expect(countryFromPhone(null)).toBe('IN');
  });

  test('chip labels and icons', () => {
    expect(locationChipLabel({ label: 'Home', placeName: 'Hyderabad', radiusKm: 25, countryCode: 'IN' })).toBe('Home · Hyderabad · 25 km');
    expect(locationChipLabel({ label: 'Lagos', placeName: 'Lagos', radiusKm: 50, countryCode: 'NG' })).toBe('Lagos · 50 km');
    expect(locationIcon({ label: 'Current location', isCurrentLocation: true })).toBe('📡');
    expect(locationIcon({ label: 'Work', isCurrentLocation: false })).toBe('💼');
    expect(radiusSummary(10, 'Bangalore')).toBe('Show parts within 10 km of Bangalore');
  });

  test('"12 km away" and the empty-state message', () => {
    expect(formatDistance(0)).toBe('< 1 km');
    expect(formatDistance(8.5)).toBe('8.5 km');
    expect(formatDistance(9.0)).toBe('9 km');
    expect(formatDistance(12.4)).toBe('12 km');
    expect(noPartsNearMessage({ label: 'Home', radiusKm: 25, countryCode: null })).toBe('No parts within 25 km of Home – try a bigger radius.');
  });

  test('miles in the USA, km elsewhere', () => {
    expect(radiusOptionsKm('US')).toEqual([8, 16, 40, 80, 161]);
    expect(radiusOptionsKm('US').map(km => formatRadius(km, 'US'))).toEqual(['5 mi', '10 mi', '25 mi', '50 mi', '100 mi']);
    expect(radiusOptionsKm('IE')).toEqual([5, 10, 25, 50, 100, 200]);
    expect(formatRadius(25, 'IE')).toBe('25 km');
    expect(nearestRadiusKm(50, 'US')).toBe(40);
    expect(nearestRadiusKm(40, 'IN')).toBe(50);
    expect(formatDistance(0.5, 'US')).toBe('< 1 mi');
    expect(formatDistance(16.09, 'US')).toBe('10 mi');
    expect(formatDistance(12.4, 'NG')).toBe('12 km');
    expect(locationChipLabel({ label: 'Home', placeName: 'Chicago', radiusKm: 40, countryCode: 'US' })).toBe('Home · Chicago · 25 mi');
    expect(radiusSummary(80, 'Houston', 'US')).toBe('Show parts within 50 mi of Houston');
  });

  test('currency follows the country', () => {
    expect(formatMoney(1500, 'US')).toBe('$1,500');
    expect(formatMoney(1500, 'IE')).toBe('€1,500');
    expect(formatMoney(1500, 'NG')).toBe('₦1,500');
    expect(formatMoney(1500, 'IN')).toBe('₹1,500');
    expect(formatMoney(45, 'IE', 2)).toBe('€45.00');
    expect(formatMoney(300, null)).toBe('₹300');
  });

  test('search results keep location and distance', () => {
    const a = mapRawDtoToAuction({
      id: 'ford-gt', make: 'Ford', model: 'GT', year: 2020, seller: 'bob',
      locationName: 'Hyderabad', latitude: 17.385, longitude: 78.4867, distanceKm: 13.5,
    });
    expect(a.locationName).toBe('Hyderabad');
    expect(a.distanceKm).toBe(13.5);
    expect(a.highBidder).toBe('');
    const old = mapRawDtoToAuction({ id: 'x', make: 'BMW', model: 'X1', seller: 'alice' });
    expect(old.distanceKm).toBeUndefined();
  });
});
