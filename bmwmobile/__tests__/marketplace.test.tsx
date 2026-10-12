import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import { MarketplaceSearchScreen, categoryLabel } from '../src/screens/MarketplaceSearchScreen';
import { ApiService } from '../src/services/api';
import { MarketApi } from '../src/services/marketApi';

jest.setTimeout(20000);

const near = { latitude: 17.385, longitude: 78.4867, radiusKm: 60 };

// The real context memoizes these objects, so the mock returns the same ones on every render.
const mockLocations = {
  status: 'ready',
  location: { latitude: 17.385, longitude: 78.4867, city: 'Hyderabad', countryCode: 'IN', radiusKm: 60, radiusValue: 60, radiusUnit: 'km', updatedAt: '' },
  locationOff: false,
  country: 'IN',
  near: { latitude: 17.385, longitude: 78.4867, radiusKm: 60 },
};
jest.mock('../src/location/LocationContext', () => ({ useLocations: () => mockLocations }));
jest.mock('../src/services/api', () => {
  const actual = jest.requireActual('../src/services/api');
  return { ...actual, ApiService: { getFilterOptions: jest.fn() } };
});
jest.mock('../src/services/marketApi', () => ({ MarketApi: { search: jest.fn(), suggest: jest.fn() } }));

const hit = {
  id: 'a1', title: '2016–2020 Toyota Hilux Front Bumper – Genuine OEM – Used (Grade A)', kind: 'VehicleSpecificPart',
  fitmentSummary: 'Fits Toyota Hilux 2016–2020', badge: 'Fits your 2016 Toyota Hilux', condition: 'Used', usedGrade: 'A',
  make: 'Toyota', model: 'Hilux', year: 2016, seller: 's', startingPrice: 14000, currentHighBid: null, currentPrice: 14000,
  bidCount: 0, auctionEnd: new Date(Date.now() + 86400000).toISOString(), status: 'Live', imageUrl: '', distanceKm: 6.2, matchTier: 1,
};

const search = MarketApi.search as jest.Mock;
const suggest = MarketApi.suggest as jest.Mock;
const getFilterOptions = ApiService.getFilterOptions as jest.Mock;
const navigation = { navigate: jest.fn() } as any;

const result = (results: unknown[]) => ({ results, totalCount: results.length, pageCount: 1, chips: [], keywords: '', facets: {} });

beforeEach(() => {
  search.mockReset().mockResolvedValue(result([hit]));
  suggest.mockReset().mockResolvedValue([]);
  getFilterOptions.mockReset().mockResolvedValue({
    categories: [{ value: 'Body – Exterior', count: 3 }, { value: 'Wheels & Tyres', count: 1 }],
    makes: [{ value: 'Toyota', count: 3 }],
    models: [{ make: 'Toyota', model: 'Hilux', count: 2 }, { make: 'Maruti Suzuki', model: 'Swift', count: 1 }],
    totalCount: 4,
  });
  navigation.navigate.mockReset();
});

/** All text of a node, including nested Text. */
const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');

const press = async (tree: ReactTestRenderer.ReactTestRenderer, label: string) => {
  let node: ReactTestRenderer.ReactTestInstance | null = tree.root.find(n => (n.type as unknown) === 'Text' && textOf(n) === label);
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
};

const mounted: ReactTestRenderer.ReactTestRenderer[] = [];
afterEach(() => {
  mounted.splice(0).forEach(tree => ReactTestRenderer.act(() => tree.unmount()));
});

const render = async () => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<MarketplaceSearchScreen navigation={navigation} route={{ key: 'm', name: 'Marketplace' } as any} />);
  });
  mounted.push(tree!);
  return tree!;
};

const lastQuery = () => search.mock.calls[search.mock.calls.length - 1][0];

test('pills come from the live listings near the user; results show fitment, badge, price and distance', async () => {
  const tree = await render();
  const t = texts(tree);

  expect(getFilterOptions).toHaveBeenCalledWith(near, 'IN');
  expect(t).toContain('Body – Exterior');
  expect(t).toContain('Toyota Hilux');
  expect(t).not.toContain('M4 Competition');
  expect(t).toContain('1 part found within 60 km');
  expect(t).toContain('Fits Toyota Hilux 2016–2020');
  expect(t).toContain('✓ Fits your 2016 Toyota Hilux');
  expect(t).toContain('₹14,000');
  expect(t).toContain('6.2 km away');
  expect(t).not.toMatch(/container|port/i);
  expect(search).toHaveBeenCalledWith(expect.objectContaining({ sort: 'best' }), near, 'IN');
});

test('category and model pills are sent to the search API, combine, and "All" clears them', async () => {
  const tree = await render();

  await press(tree, 'Body – Exterior');
  expect(lastQuery()).toEqual(expect.objectContaining({ category: 'Body – Exterior', make: undefined }));

  await press(tree, 'Toyota Hilux');
  expect(lastQuery()).toEqual(expect.objectContaining({ category: 'Body – Exterior', make: 'Toyota', model: 'Hilux' }));

  await press(tree, 'All');
  expect(lastQuery()).toEqual(expect.objectContaining({ category: undefined, make: 'Toyota' }));

  await press(tree, 'All Models');
  expect(lastQuery()).toEqual(expect.objectContaining({ category: undefined, make: undefined, model: undefined }));
});

test('suggestions while typing turn into chips that filter the search', async () => {
  suggest.mockResolvedValue([
    {
      group: 'parts',
      label: 'Front bumper – 2016 Toyota Hilux',
      chips: [
        { type: 'partType', value: 'Bumper', label: 'Front bumper' },
        { type: 'make', value: 'Toyota', label: 'Toyota' },
        { type: 'model', value: 'Hilux', label: 'Hilux' },
        { type: 'year', value: '2016', label: '2016' },
      ],
    },
  ]);
  const tree = await render();
  const input = tree.root.find(n => n.props.accessibilityLabel === 'Search' && n.props.onChangeText);

  await ReactTestRenderer.act(async () => input.props.onFocus());
  await ReactTestRenderer.act(async () => input.props.onChangeText('2016 hilux front bum'));
  await ReactTestRenderer.act(async () => new Promise(resolve => setTimeout(resolve, 300)));

  expect(suggest).toHaveBeenCalledWith('2016 hilux front bum', 'IN');
  expect(texts(tree)).toContain('PARTS');
  await press(tree, 'Front bumper – 2016 Toyota Hilux');

  expect(lastQuery()).toEqual(expect.objectContaining({ partType: 'Bumper', make: 'Toyota', model: 'Hilux', year: 2016, q: 'front' }));
  expect(texts(tree)).toContain('Front bumper ✕');
  expect(texts(tree)).toContain('Hilux ✕');
});

test('no matches → "no parts available" message and a pre-filled custom order', async () => {
  search.mockResolvedValue(result([]));
  const tree = await render();
  await press(tree, 'Toyota Hilux');

  const t = texts(tree);
  expect(t).toContain('There are no parts available for this search.');
  expect(t).toContain('Create a custom order for your part and get notified when the part is available.');

  await press(tree, 'Create custom order');
  expect(navigation.navigate).toHaveBeenCalledWith('OrderPartRequest', expect.objectContaining({ prefillMake: 'Toyota', prefillModel: 'Hilux' }));
});

test('category labels', () => {
  expect(categoryLabel('WheelsAndTyres')).toBe('Wheels & Tyres');
  expect(categoryLabel('Body')).toBe('Body');
});
