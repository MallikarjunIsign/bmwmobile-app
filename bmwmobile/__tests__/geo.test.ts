import { mapRawDtoToAuction } from '../src/services/api';
import {
  countryFromPhone,
  DEFAULT_RADIUS_KM,
  formatDistance,
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
    expect(countryFromPhone(null)).toBe('IN');
  });

  test('chip labels and icons', () => {
    expect(locationChipLabel({ label: 'Home', placeName: 'Hyderabad', radiusKm: 25 })).toBe('Home · Hyderabad · 25 km');
    expect(locationChipLabel({ label: 'Lagos', placeName: 'Lagos', radiusKm: 50 })).toBe('Lagos · 50 km');
    expect(locationIcon({ label: 'Current location', isCurrentLocation: true })).toBe('📡');
    expect(locationIcon({ label: 'Work', isCurrentLocation: false })).toBe('💼');
    expect(radiusSummary(10, 'Bangalore')).toBe('Show parts within 10 km of Bangalore');
  });

  test('"12 km away" and the empty-state message', () => {
    expect(formatDistance(0)).toBe('< 1 km');
    expect(formatDistance(8.5)).toBe('8.5 km');
    expect(formatDistance(9.0)).toBe('9 km');
    expect(formatDistance(12.4)).toBe('12 km');
    expect(noPartsNearMessage({ label: 'Home', radiusKm: 25 })).toBe('No parts within 25 km of Home – try a bigger radius.');
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
