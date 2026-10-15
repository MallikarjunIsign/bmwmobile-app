import React from 'react';
import { View } from 'react-native';
import ReactTestRenderer from 'react-test-renderer';
import { MarketplaceSearchScreen, categoryLabel } from '../src/screens/MarketplaceSearchScreen';
import { HomeScreen } from '../src/screens/HomeScreen';
import { BrowseProvider } from '../src/search/BrowseContext';
import { MarketApi } from '../src/services/marketApi';
import { categoryIcon } from '../src/utils/categories';

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
jest.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ user: { fullName: 'Ravi Kumar', role: 'buyer' }, status: 'signedIn' }) }));
jest.mock('../src/services/marketApi', () => ({ MarketApi: { search: jest.fn(), suggest: jest.fn() } }));

const hit = {
  id: 'a1', title: '2016–2020 Toyota Hilux Front Bumper – Genuine OEM – Used (Grade A)', kind: 'VehicleSpecificPart',
  fitmentSummary: 'Fits Toyota Hilux 2016–2020', badge: 'Fits your 2016 Toyota Hilux', condition: 'Used', usedGrade: 'A',
  make: 'Toyota', model: 'Hilux', year: 2016, seller: 's', startingPrice: 14000, currentHighBid: null, currentPrice: 14000,
  bidCount: 0, auctionEnd: new Date(Date.now() + 86400000).toISOString(), status: 'Live', imageUrl: '', distanceKm: 6.2, matchTier: 1,
};
const car = { ...hit, id: 'v1', title: '2018 Toyota Hilux', kind: 'Vehicle', badge: null, fitmentSummary: null, startingPrice: 1500000, currentPrice: 1500000 };

const partFacets = {
  category: [{ value: 'Body – Exterior', count: 3 }, { value: 'Brakes', count: 24 }, { value: 'Lighting', count: 12 }],
  partType: [{ value: 'Bumper', count: 2 }, { value: 'Bonnet', count: 1 }],
  model: [{ value: 'Toyota|Hilux', count: 2 }, { value: 'Maruti Suzuki|Swift', count: 1 }],
  make: [{ value: 'Toyota', count: 3 }],
};
const vehicleFacets = { make: [{ value: 'Toyota', count: 12 }, { value: 'Honda', count: 8 }], model: [{ value: 'Toyota|Hilux', count: 5 }] };

const search = MarketApi.search as jest.Mock;
const suggest = MarketApi.suggest as jest.Mock;
const navigation = { navigate: jest.fn() } as any;

const result = (results: unknown[], facets: object, totalCount = results.length) =>
  ({ results, totalCount, pageCount: 1, chips: [], keywords: '', facets });

beforeEach(() => {
  search.mockReset().mockImplementation(async (q: { itemType?: string }) =>
    q.itemType === 'Vehicle' ? result([car], vehicleFacets, 128) : result([hit], partFacets));
  suggest.mockReset().mockResolvedValue([]);
  navigation.navigate.mockReset();
});

/** All text of a node, including nested Text. */
const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');

const press = async (tree: ReactTestRenderer.ReactTestRenderer, label: string, within?: ReactTestRenderer.ReactTestInstance) => {
  let node: ReactTestRenderer.ReactTestInstance | null = (within ?? tree.root).find(n => (n.type as unknown) === 'Text' && textOf(n) === label);
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
};
const pressLabel = async (tree: ReactTestRenderer.ReactTestRenderer, label: string) => {
  const node = tree.root.find(n => n.props.accessibilityLabel === label && n.props.onPress);
  await ReactTestRenderer.act(async () => node.props.onPress());
};

const mounted: ReactTestRenderer.ReactTestRenderer[] = [];
afterEach(async () => {
  for (const tree of mounted.splice(0)) await ReactTestRenderer.act(async () => tree.unmount());
});

const mount = async (element: React.ReactElement) => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<BrowseProvider>{element}</BrowseProvider>);
  });
  mounted.push(tree!);
  return tree!;
};
const market = () => <MarketplaceSearchScreen navigation={navigation} route={{ key: 'm', name: 'Marketplace' } as any} />;
const home = () => <HomeScreen navigation={navigation} route={{ key: 'h', name: 'Home' } as any} />;

const lastQuery = () => search.mock.calls[search.mock.calls.length - 1][0];

const typeAndPick = async (tree: ReactTestRenderer.ReactTestRenderer, text: string, suggestion: string, within?: ReactTestRenderer.ReactTestInstance) => {
  const input = (within ?? tree.root).find(n => n.props.accessibilityLabel === 'Search' && n.props.onChangeText);
  await ReactTestRenderer.act(async () => input.props.onFocus());
  await ReactTestRenderer.act(async () => input.props.onChangeText(text));
  await ReactTestRenderer.act(async () => new Promise(resolve => setTimeout(resolve, 300)));
  await press(tree, suggestion, within);
};

const bumperSuggestion = {
  group: 'parts',
  label: 'Front bumper – 2016 Toyota Hilux',
  chips: [
    { type: 'partType', value: 'Bumper', label: 'Front bumper' },
    { type: 'make', value: 'Toyota', label: 'Toyota' },
    { type: 'model', value: 'Hilux', label: 'Hilux' },
    { type: 'year', value: '2016', label: '2016' },
  ],
};

test('market: pills and counts come from the same search; results show fitment, badge, price and distance', async () => {
  const tree = await mount(market());
  const t = texts(tree);

  expect(search).toHaveBeenCalledWith(expect.objectContaining({ itemType: 'Part', saleType: undefined, sort: 'best' }), near, 'IN');
  expect(t).toContain('Brakes (24)');
  expect(t).toContain('Lighting (12)');
  expect(t).toContain('Toyota Hilux (2)');
  expect(t).toContain('1 part found · pickup within 60 km, shipping India-wide');
  expect(t).toContain('Fits Toyota Hilux 2016–2020');
  expect(t).toContain('✓ Fits your 2016 Toyota Hilux');
  expect(t).toContain('₹14,000');
  expect(t).toContain('6.2 km away');
  expect(t).not.toMatch(/container|port/i);
});

test('market: a category pill filters, then part types show as chips with counts; "All categories" clears both', async () => {
  const tree = await mount(market());

  await press(tree, 'Body – Exterior (3)');
  expect(lastQuery()).toEqual(expect.objectContaining({ category: 'Body – Exterior', partType: undefined }));
  expect(texts(tree)).toContain('Bumper (2)');

  await press(tree, 'Bumper (2)');
  expect(lastQuery()).toEqual(expect.objectContaining({ category: 'Body – Exterior', partType: 'Bumper' }));

  await press(tree, 'All categories');
  expect(lastQuery()).toEqual(expect.objectContaining({ category: undefined, partType: undefined }));
});

test('THE SEARCH BUG: picking a suggestion ADDS its chips and every active filter goes with the search', async () => {
  suggest.mockResolvedValue([bumperSuggestion]);
  const tree = await mount(market());

  // Filters first: Direct sale + a category.
  await press(tree, 'Direct sale');
  await press(tree, 'Body – Exterior (3)');
  await typeAndPick(tree, '2016 hilux front bum', 'Front bumper – 2016 Toyota Hilux');

  // Suggestions were asked for parts only.
  // …and only from listings this buyer can see (their location and radius).
  expect(suggest).toHaveBeenCalledWith('2016 hilux front bum', 'IN', 'Part', near);
  // One request with the text's chips AND all filters (Parts, Direct sale, category, radius).
  expect(lastQuery()).toEqual(expect.objectContaining({
    itemType: 'Part', saleType: 'FixedPrice', category: 'Body – Exterior',
    partType: 'Bumper', make: 'Toyota', model: 'Hilux', year: 2016, q: 'front',
  }));
  expect(search.mock.calls[search.mock.calls.length - 1][1]).toEqual(near);
  expect(texts(tree)).toContain('Front bumper ✕');

  // A second suggestion adds to the chips (doesn't clear them).
  suggest.mockResolvedValue([{ group: 'partNumbers', label: '52119-0K935', chips: [{ type: 'partNumber', value: '521190K935', label: '52119-0K935' }] }]);
  await typeAndPick(tree, '52119', '52119-0K935');
  expect(lastQuery()).toEqual(expect.objectContaining({ partType: 'Bumper', make: 'Toyota', saleType: 'FixedPrice', category: 'Body – Exterior' }));
  expect(lastQuery().q).toContain('52119-0K935');
});

test('market: ✕ clears everything in the search box (text, chips, the category chip) but keeps Auction / Direct sale; "Clear all" clears both', async () => {
  suggest.mockResolvedValue([bumperSuggestion]);
  const tree = await mount(market());
  await press(tree, 'Auction');
  await press(tree, 'Brakes (24)');
  await typeAndPick(tree, 'front bum', 'Front bumper – 2016 Toyota Hilux');

  await pressLabel(tree, 'Clear search');
  expect(lastQuery()).toEqual(expect.objectContaining({ partType: undefined, make: undefined, q: undefined, saleType: 'Auction', category: undefined }));

  await typeAndPick(tree, 'front bum', 'Front bumper – 2016 Toyota Hilux');
  await pressLabel(tree, 'Clear all');
  expect(lastQuery()).toEqual(expect.objectContaining({ partType: undefined, q: undefined, saleType: undefined, category: undefined, itemType: 'Part' }));
  expect(texts(tree)).not.toContain('Clear all');
});

test('market: Vehicles → vehicle results, make pills with counts, vehicle-only suggestions', async () => {
  const tree = await mount(market());
  await press(tree, 'Vehicles');

  expect(lastQuery()).toEqual(expect.objectContaining({ itemType: 'Vehicle', category: undefined }));
  let t = texts(tree);
  expect(t).toContain('128 vehicles found · pickup within 60 km, shipping India-wide');
  expect(t).toContain('Toyota (12)');
  expect(t).toContain('Honda (8)');

  await press(tree, 'Toyota (12)');
  expect(lastQuery()).toEqual(expect.objectContaining({ itemType: 'Vehicle', make: 'Toyota' }));
  t = texts(tree);
  expect(t).toContain('Hilux (5)');

  const input = tree.root.find(n => n.props.accessibilityLabel === 'Search' && n.props.onChangeText);
  await ReactTestRenderer.act(async () => input.props.onFocus());
  await ReactTestRenderer.act(async () => input.props.onChangeText('hilux'));
  await ReactTestRenderer.act(async () => new Promise(resolve => setTimeout(resolve, 300)));
  expect(suggest).toHaveBeenCalledWith('hilux', 'IN', 'Vehicle', near);
});

test('market: no matches → "no parts available" message and a pre-filled custom order', async () => {
  search.mockResolvedValue(result([], partFacets));
  const tree = await mount(market());
  await press(tree, 'Toyota Hilux (2)');

  const t = texts(tree);
  expect(t).toContain('There are no parts available for this search.');

  await press(tree, 'Create custom order');
  expect(navigation.navigate).toHaveBeenCalledWith('OrderPartRequest', expect.objectContaining({ prefillMake: 'Toyota', prefillModel: 'Hilux' }));
});

test('home: Parts / Vehicles and All / Auction / Direct sale on top; no garage chips; categories with counts', async () => {
  const tree = await mount(home());
  const t = texts(tree);

  expect(t).toContain('Parts');
  expect(t).toContain('Vehicles');
  expect(t).toContain('Direct sale');
  expect(t).not.toMatch(/All vehicles|Add your vehicle|My Garage|FILTER BY YOUR GARAGE/);
  expect(t).toContain('SHOP BY CATEGORY');
  expect(t).toContain('Brakes (24)');
  expect(t).toContain('Lighting (12)');
  expect(search).toHaveBeenCalledWith(expect.objectContaining({ itemType: 'Part' }), near, 'IN');

  // The sale-type filter applies to the counts below it.
  await press(tree, 'Direct sale');
  expect(lastQuery()).toEqual(expect.objectContaining({ itemType: 'Part', saleType: 'FixedPrice' }));
});

test('home: Vehicles shows the total near you and make chips with counts', async () => {
  const tree = await mount(home());
  await press(tree, 'Vehicles');

  const t = texts(tree);
  expect(t).toContain('128 vehicles near you');
  expect(t).toContain('Toyota (12)');
  expect(t).not.toContain('SHOP BY CATEGORY');

  await press(tree, 'Toyota (12)');
  // The list is narrowed to Toyota; the chips keep every make (a second query without the make).
  expect(search).toHaveBeenCalledWith(expect.objectContaining({ itemType: 'Vehicle', make: 'Toyota' }), near, 'IN');
  expect(texts(tree)).toContain('Honda (8)');
});

test('home and market share ONE state: a tile or a suggestion on Home filters Home at once, and Market shows the same', async () => {
  suggest.mockResolvedValue([bumperSuggestion]);
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(
      <BrowseProvider>
        <View testID="home">{home()}</View>
        <View testID="market">{market()}</View>
      </BrowseProvider>,
    );
  });
  mounted.push(tree!);
  const homeView = tree!.root.find(n => n.props.testID === 'home');
  const marketView = tree!.root.find(n => n.props.testID === 'market');
  const textIn = (view: ReactTestRenderer.ReactTestInstance) => view.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');

  await press(tree!, 'Auction', homeView);
  await press(tree!, 'Brakes (24)', homeView);
  // The tile stays on Home: a category chip, and the search (Home's list and counts) has the category.
  expect(navigation.navigate).not.toHaveBeenCalledWith('Marketplace');
  expect(lastQuery()).toEqual(expect.objectContaining({ itemType: 'Part', saleType: 'Auction', category: 'Brakes' }));
  expect(textIn(homeView)).toContain('Brakes ✕');
  expect(textIn(marketView)).toContain('Brakes ✕');

  // A suggestion from Home's box keeps the filters and adds the chips – on both screens.
  await typeAndPick(tree!, 'front bum', 'Front bumper – 2016 Toyota Hilux', homeView);
  expect(lastQuery()).toEqual(expect.objectContaining({ saleType: 'Auction', category: 'Brakes', partType: 'Bumper', model: 'Hilux' }));
  expect(textIn(homeView)).toContain('Front bumper ✕');
  expect(textIn(marketView)).toContain('Front bumper ✕');

  // "See All" opens Market with that same search.
  await press(tree!, 'See All (1) →', homeView);
  expect(navigation.navigate).toHaveBeenCalledWith('Marketplace');
});

test('category labels and icons', () => {
  expect(categoryLabel('WheelsAndTyres')).toBe('Wheels & Tyres');
  expect(categoryLabel('Body')).toBe('Body');
  expect(categoryIcon('Brakes')).toBe('🛑');
  expect(categoryIcon('Lighting')).toBe('💡');
  expect(categoryIcon('Something else')).toBe('🔧');
});
