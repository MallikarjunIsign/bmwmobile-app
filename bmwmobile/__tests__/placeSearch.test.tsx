import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import { PlaceSearch } from '../src/components/PlaceSearch';
import { AddressFormScreen } from '../src/screens/AddressFormScreen';
import { PlacesApi } from '../src/services/placesApi';
import { getCurrentPosition } from '../src/utils/gps';
import { placeLabel } from '../src/utils/geo';
import type { PlaceDetails } from '../src/types/location';

jest.mock('../src/services/placesApi', () => {
  const actual = jest.requireActual('../src/services/placesApi');
  return { ...actual, PlacesApi: { autocomplete: jest.fn(), details: jest.fn(), reverse: jest.fn() } };
});
jest.mock('../src/utils/gps', () => {
  const actual = jest.requireActual('../src/utils/gps');
  return { ...actual, getCurrentPosition: jest.fn() };
});
const mockLocations = {
  addresses: [] as unknown[],
  saveAddress: jest.fn(),
  country: 'IN',
  location: { latitude: 17.385, longitude: 78.4867, city: 'Hyderabad', countryCode: 'IN', radiusKm: 200, radiusValue: 200, radiusUnit: 'km', updatedAt: '' },
};
jest.mock('../src/location/LocationContext', () => ({ useLocations: () => mockLocations }));

const api = PlacesApi as unknown as Record<'autocomplete' | 'details' | 'reverse', jest.Mock>;
const position = getCurrentPosition as jest.Mock;

const KUKATPALLY: PlaceDetails = {
  placeId: 'ChIJ-kukatpally', formattedAddress: 'Kukatpally, Hyderabad, Telangana 500072, India', lat: 17.4948, lon: 78.3996,
  label: 'Kukatpally, Hyderabad', name: 'Kukatpally', street: null, area: 'Kukatpally', city: 'Hyderabad', state: 'Telangana',
  stateCode: 'TS', countryCode: 'IN', postcode: '500072', supported: true, message: null, source: 'google',
};
const suggestions = {
  source: 'google',
  suggestions: [
    { placeId: 'ChIJ-kukatpally', primaryText: 'Kukatpally', secondaryText: 'Hyderabad, Telangana, India' },
    { placeId: 'ChIJ-kphb', primaryText: 'KPHB Colony', secondaryText: 'Kukatpally, Hyderabad, Telangana, India' },
  ],
};

const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');
const searchBox = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.find(n => (n.type as unknown) === 'TextInput' && n.props.accessibilityLabel === 'Search location');
const press = async (tree: ReactTestRenderer.ReactTestRenderer, match: (t: string) => boolean) => {
  let node: ReactTestRenderer.ReactTestInstance | null = tree.root.find(n => (n.type as unknown) === 'Text' && match(textOf(n)));
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
};
const type = async (tree: ReactTestRenderer.ReactTestRenderer, text: string) =>
  ReactTestRenderer.act(async () => searchBox(tree).props.onChangeText(text));
/** Lets the 300 ms pause pass and the answer arrive. */
const pause = async (ms = 300) =>
  ReactTestRenderer.act(async () => {
    jest.advanceTimersByTime(ms);
    await Promise.resolve();
  });

const renderSearch = async (props: Partial<React.ComponentProps<typeof PlaceSearch>> = {}) => {
  const onPick = jest.fn();
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<PlaceSearch value={null} onPick={onPick} near={{ latitude: 17.385, longitude: 78.4867 }} {...props} />);
  });
  return { tree: tree!, onPick };
};

beforeEach(() => {
  jest.useFakeTimers();
  Object.values(api).forEach(m => m.mockReset());
  position.mockReset();
  mockLocations.saveAddress.mockReset();
  api.autocomplete.mockResolvedValue(suggestions);
  api.details.mockResolvedValue(KUKATPALLY);
});
afterEach(() => jest.useRealTimers());

test('nothing is searched before 3 characters', async () => {
  const { tree } = await renderSearch();
  await type(tree, 'Ku');
  await pause(1000);
  expect(api.autocomplete).not.toHaveBeenCalled();
  expect(texts(tree)).not.toContain('No places found');
});

test('debounce: one search after a 300 ms pause in typing, with the session token and the user location', async () => {
  const { tree } = await renderSearch();
  await type(tree, 'Kuk');
  await pause(100);
  await type(tree, 'Kuka');
  await pause(100);
  await type(tree, 'Kukat');
  await pause(299);
  expect(api.autocomplete).not.toHaveBeenCalled();
  await pause(1);
  expect(api.autocomplete).toHaveBeenCalledTimes(1);
  const [q, token, near] = api.autocomplete.mock.calls[0];
  expect(q).toBe('Kukat');
  expect(token).toMatch(/^[0-9a-f-]{36}$/);
  expect(near).toEqual({ latitude: 17.385, longitude: 78.4867 });

  // Main text bold, secondary grey; the Google attribution under the list.
  const t = texts(tree);
  expect(t).toContain('Kukatpally | Hyderabad, Telangana, India');
  expect(t).toContain('powered by Google');
});

test('the same session token for the whole search and the details call; a new one after picking', async () => {
  const { tree, onPick } = await renderSearch();
  await type(tree, 'Kuka');
  await pause();
  await type(tree, 'Kukatpally');
  await pause();
  const token = api.autocomplete.mock.calls[0][1];
  expect(api.autocomplete.mock.calls[1][1]).toBe(token);

  await press(tree, x => x === 'Kukatpally');
  expect(api.details).toHaveBeenCalledWith('ChIJ-kukatpally', token);
  expect(onPick).toHaveBeenCalledWith(KUKATPALLY);

  await type(tree, 'Gachibowli');
  await pause();
  expect(api.autocomplete.mock.calls[2][1]).not.toBe(token);
});

test('no attribution for the built-in city list; "No places found" when nothing matches', async () => {
  api.autocomplete.mockResolvedValueOnce({ source: 'cities', suggestions: [{ placeId: 'city:IN:Hyderabad', primaryText: 'Hyderabad', secondaryText: 'India' }] });
  const { tree } = await renderSearch();
  await type(tree, 'Hyder');
  await pause();
  expect(texts(tree)).toContain('Hyderabad');
  expect(texts(tree)).not.toContain('powered by Google');

  api.autocomplete.mockResolvedValueOnce({ source: 'google', suggestions: [] });
  await type(tree, 'Zzzzqq');
  await pause();
  expect(texts(tree)).toContain('No places found');
});

test('a place outside our countries is not picked', async () => {
  api.details.mockResolvedValueOnce({ ...KUKATPALLY, countryCode: 'GB', supported: false, message: "We don't operate in this country yet" });
  const { tree, onPick } = await renderSearch();
  await type(tree, 'Kukatpally');
  await pause();
  await press(tree, x => x === 'Kukatpally');
  expect(onPick).not.toHaveBeenCalled();
  expect(texts(tree)).toContain("We don't operate in this country yet");
});

test('too many searches: the friendly message', async () => {
  api.autocomplete.mockRejectedValueOnce(new Error("You're searching very fast. Please wait a moment and try again."));
  const { tree } = await renderSearch();
  await type(tree, 'Kukatpally');
  await pause();
  expect(texts(tree)).toContain("You're searching very fast");
});

test('"Use my current location": GPS → area, city and country', async () => {
  position.mockResolvedValue({ latitude: 17.4949, longitude: 78.3997 });
  api.reverse.mockResolvedValue({ ...KUKATPALLY, placeId: 'ChIJ-road', formattedAddress: '' });
  const { tree, onPick } = await renderSearch();
  await press(tree, x => x === '🎯 Use my current location');
  expect(position).toHaveBeenCalledWith({ prompt: true });
  expect(api.reverse).toHaveBeenCalledWith(17.4949, 78.3997);
  const picked = onPick.mock.calls[0][0] as PlaceDetails;
  expect(picked).toMatchObject({ label: 'Kukatpally, Hyderabad', lat: 17.4949, lon: 78.3997 });

  // Shown as "Kukatpally, Hyderabad" with the country underneath.
  await ReactTestRenderer.act(async () => tree.update(<PlaceSearch value={picked} onPick={onPick} />));
  expect(texts(tree)).toContain('📍 Kukatpally, Hyderabad | Kukatpally, Hyderabad, India');
});

test('the location label is area + city, else the city', () => {
  expect(placeLabel({ area: 'Kukatpally', city: 'Hyderabad' })).toBe('Kukatpally, Hyderabad');
  expect(placeLabel({ area: null, city: 'Katy' })).toBe('Katy');
  expect(placeLabel({ area: 'Kukatpally', city: 'Kukatpally, Hyderabad' })).toBe('Kukatpally, Hyderabad');
  expect(placeLabel(null)).toBeNull();
});

test('Add location: picking a suggestion fills the form, the radius stays, and the place is saved', async () => {
  mockLocations.saveAddress.mockResolvedValue(undefined);
  const navigation = { goBack: jest.fn() } as any;
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<AddressFormScreen navigation={navigation} route={{ key: 'a', name: 'AddressForm', params: {} } as any} />);
  });
  // A radius chosen before picking the place stays.
  await press(tree!, x => x === '50 km');

  await type(tree!, 'Kukatpally');
  await pause();
  await press(tree!, x => x === 'Kukatpally');

  const inputs = tree!.root.findAll(n => (n.type as unknown) === 'TextInput');
  expect(inputs.some(i => i.props.value === 'Kukatpally')).toBe(true); // area
  expect(inputs.some(i => i.props.value === '500072')).toBe(true);     // PIN code
  expect(texts(tree!)).toContain('📍 Kukatpally, Hyderabad');
  expect(tree!.root.findAll(n => n.props.testID === 'radius-picker').length).toBeGreaterThan(0);

  const street = inputs.find(i => i.props.placeholder === 'Flat 402, Sai Residency, Road No. 2')!;
  await ReactTestRenderer.act(async () => street.props.onChangeText('Flat 12, Road 4'));
  await press(tree!, x => x === 'SAVE AND USE THIS ADDRESS');

  expect(mockLocations.saveAddress).toHaveBeenCalledWith(
    expect.objectContaining({
      placeName: 'Kukatpally, Hyderabad', latitude: 17.4948, longitude: 78.3996, countryCode: 'IN', area: 'Kukatpally',
      city: 'Hyderabad', state: 'Telangana', postalCode: '500072', placeId: 'ChIJ-kukatpally',
      formattedAddress: 'Kukatpally, Hyderabad, Telangana 500072, India', addressLine: 'Flat 12, Road 4', radiusValue: 50, makePrimary: true,
    }),
    undefined,
  );
  expect(navigation.goBack).toHaveBeenCalled();
});
