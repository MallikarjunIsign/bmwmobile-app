import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import { LocationProvider, useLocations } from '../src/location/LocationContext';
import { ApiService } from '../src/services/api';
import { getCurrentPosition, GpsError } from '../src/utils/gps';
import { LocationApi } from '../src/services/locationApi';
import AsyncStorage from '@react-native-async-storage/async-storage';

jest.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ status: 'signedIn', user: { id: 'u1', phoneNumber: '+12015550123' } }),
}));
jest.mock('../src/services/api', () => ({ ApiService: { reverseGeocode: jest.fn() } }));
jest.mock('../src/utils/gps', () => {
  const actual = jest.requireActual('../src/utils/gps');
  return { ...actual, getCurrentPosition: jest.fn(), openLocationSettings: jest.fn() };
});

jest.mock('../src/services/locationApi', () => ({
  LocationApi: { list: jest.fn(), setRadius: jest.fn(), setCurrent: jest.fn(), setPrimary: jest.fn() },
}));

const reverseGeocode = ApiService.reverseGeocode as jest.Mock;
const position = getCurrentPosition as jest.Mock;
const locationApi = LocationApi as unknown as Record<string, jest.Mock>;

const HYDERABAD = { latitude: 17.385, longitude: 78.4867 };
const hyderabadGeo = { ...HYDERABAD, city: 'Hyderabad', countryCode: 'IN', supported: true, radiusValue: 200, radiusUnit: 'km', radiusKm: 200 };
const HOUSTON = { latitude: 29.7604, longitude: -95.3698 };
const houstonGeo = { ...HOUSTON, city: 'Houston', countryCode: 'US', supported: true, radiusValue: 200, radiusUnit: 'mi', radiusKm: 321.87 };
const gpsRow = (radiusValue: number) => ({
  id: 'g1', label: 'Current location', placeName: 'Houston', ...HOUSTON, countryCode: 'US', isPrimary: true, isCurrentLocation: true,
  radiusValue, radiusUnit: 'mi', radiusKm: Math.round(radiusValue * 1.609344 * 100) / 100,
});

let ctx: ReturnType<typeof useLocations>;
const Probe = () => {
  ctx = useLocations();
  return null;
};

const render = async () => {
  await ReactTestRenderer.act(async () => {
    ReactTestRenderer.create(
      <LocationProvider>
        <Probe />
      </LocationProvider>,
    );
  });
  // Let the GPS read and the reverse geocode finish.
  await ReactTestRenderer.act(async () => {
    await new Promise(resolve => setTimeout(resolve, 0));
  });
};

beforeEach(() => {
  reverseGeocode.mockReset();
  position.mockReset();
  Object.values(locationApi).forEach(m => m.mockReset());
  locationApi.list.mockResolvedValue(null);
});

test('after sign-in: asks for permission, sets city, country and the 200 km radius automatically', async () => {
  position.mockResolvedValue(HYDERABAD);
  reverseGeocode.mockResolvedValue(hyderabadGeo);

  await render();

  expect(position).toHaveBeenCalledWith({ prompt: true });
  expect(ctx.location).toMatchObject({ city: 'Hyderabad', countryCode: 'IN', radiusKm: 200, radiusValue: 200, radiusUnit: 'km' });
  expect(ctx.near).toEqual({ latitude: 17.385, longitude: 78.4867, radiusKm: 200 });
  expect(ctx.country).toBe('IN');
  expect(ctx.locationOff).toBe(false);
});

test('USA: 200 miles and dollars', async () => {
  position.mockResolvedValue(HOUSTON);
  reverseGeocode.mockResolvedValue(houstonGeo);

  await render();

  expect(ctx.location).toMatchObject({ city: 'Houston', radiusValue: 200, radiusUnit: 'mi' });
  expect(ctx.near?.radiusKm).toBe(321.87);
  expect(ctx.country).toBe('US');
});

test('an older cached GPS location with 60 mi gets the default 200 mi (no radius chosen yet)', async () => {
  await AsyncStorage.setItem('@bidmyparts/auto-location/u1', JSON.stringify({
    ...HOUSTON, city: 'Houston', countryCode: 'US', radiusValue: 60, radiusUnit: 'mi', radiusKm: 96.56, updatedAt: '',
  }));
  position.mockResolvedValue(HOUSTON);
  reverseGeocode.mockResolvedValue(houstonGeo);

  await render();

  expect(ctx.location).toMatchObject({ radiusValue: 200, radiusUnit: 'mi', radiusKm: 321.87 });
  await AsyncStorage.removeItem('@bidmyparts/auto-location/u1');
});

test('changing the radius updates the search at once and saves it with the GPS location', async () => {
  position.mockResolvedValue(HOUSTON);
  reverseGeocode.mockResolvedValue(houstonGeo);
  let answer: (v: unknown) => void = () => undefined;
  locationApi.setCurrent.mockReturnValue(new Promise(resolve => { answer = resolve; }));
  await render();

  let saving: Promise<void> | undefined;
  await ReactTestRenderer.act(async () => {
    saving = ctx.setRadius(25);
  });
  // Before the server answers: the lists already use 25 mi.
  expect(ctx.near?.radiusKm).toBe(40.23);
  expect(locationApi.setCurrent).toHaveBeenCalledWith(29.7604, -95.3698, 25);

  await ReactTestRenderer.act(async () => {
    answer({ autoUpdatePrimary: true, locations: [gpsRow(25)] });
    await saving;
  });
  expect(ctx.location).toMatchObject({ radiusValue: 25, radiusUnit: 'mi' });

  // The GPS row exists now: later changes only change its radius.
  locationApi.setRadius.mockResolvedValue({ autoUpdatePrimary: true, locations: [gpsRow(500)] });
  await ReactTestRenderer.act(async () => ctx.setRadius(500));
  expect(locationApi.setRadius).toHaveBeenCalledWith('g1', 500);
  expect(ctx.location).toMatchObject({ radiusValue: 500 });
});

test('permission denied: banner + country-wide results from the phone number country', async () => {
  position.mockRejectedValue(new GpsError('Turn on location to see parts near you', 'denied'));

  await render();

  expect(ctx.locationOff).toBe(true);
  expect(ctx.near).toBeUndefined();
  expect(ctx.country).toBe('US'); // +1 phone number
  expect(reverseGeocode).not.toHaveBeenCalled();
});

test('a GPS timeout keeps the last location without the banner', async () => {
  position.mockRejectedValue(new GpsError('Finding your location took too long.', 'timeout'));

  await render();

  expect(ctx.locationOff).toBe(false);
});

test('small moves in the same city are ignored; moves over 5 km update the location', async () => {
  position.mockResolvedValue(HYDERABAD);
  reverseGeocode.mockResolvedValue(hyderabadGeo);
  await render();
  expect(reverseGeocode).toHaveBeenCalledTimes(1);

  // ~2.5 km away, still Hyderabad: location stays.
  position.mockResolvedValue({ latitude: 17.4075, longitude: 78.4867 });
  await ReactTestRenderer.act(async () => ctx.refresh());
  expect(ctx.location?.latitude).toBe(17.385);

  // ~11 km away: location moves.
  reverseGeocode.mockResolvedValue({ ...hyderabadGeo, latitude: 17.485, city: 'Hyderabad' });
  position.mockResolvedValue({ latitude: 17.485, longitude: 78.4867 });
  await ReactTestRenderer.act(async () => ctx.refresh());
  expect(ctx.location?.latitude).toBe(17.485);
});

test('backend down: keeps the coordinates with the phone country radius', async () => {
  position.mockResolvedValue(HYDERABAD);
  reverseGeocode.mockRejectedValue(new Error('Network Error'));

  await render();

  expect(ctx.location).toMatchObject({ latitude: 17.385, city: null, countryCode: null, radiusUnit: 'mi' });
  expect(ctx.near).toBeDefined();
});
