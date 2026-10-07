import {
  countryFromPhone,
  DEFAULT_RADIUS_KM,
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
});
