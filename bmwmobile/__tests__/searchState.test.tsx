import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import { BrowseProvider, categoryChip, useBrowse } from '../src/search/BrowseContext';
import { buildQuery } from '../src/hooks/useListingSearch';
import { HomeScreen } from '../src/screens/HomeScreen';
import { MarketplaceSearchScreen } from '../src/screens/MarketplaceSearchScreen';
import { MarketApi } from '../src/services/marketApi';
import type { QueryChip } from '../src/types/listing';

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

jest.setTimeout(20000);

const bumper: QueryChip = { type: 'partType', value: 'Bumper', label: 'Bumper' };
const toyota: QueryChip = { type: 'make', value: 'Toyota', label: 'Toyota' };
const hilux: QueryChip = { type: 'model', value: 'Hilux', label: 'Hilux' };
const headlamp: QueryChip = { type: 'partType', value: 'Headlamp', label: 'Headlamp' };

type Browse = ReturnType<typeof useBrowse>;

/** Renders the shared state alone; `state()` is always the latest value. */
async function mountState() {
  let latest: Browse | null = null;
  const Probe = () => {
    latest = useBrowse();
    return null;
  };
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<BrowseProvider><Probe /></BrowseProvider>);
  });
  const run = async (fn: (b: Browse) => void) => ReactTestRenderer.act(async () => fn(latest!));
  return { state: () => latest!, run, tree: tree! };
}

const query = (b: Browse) => buildQuery(b.input, null);

describe('shared search / filter state (Home + Market)', () => {
  test('a suggestion adds its chips, the query has them, and existing filters stay', async () => {
    const { state, run } = await mountState();
    await run(b => b.setSaleType('FixedPrice'));
    await run(b => b.applyChips([categoryChip('Body – Exterior')]));
    await run(b => b.applyChips([bumper]));
    await run(b => b.applyChips([toyota, hilux]));

    expect(state().chips.map(c => c.label)).toEqual(['Body – Exterior', 'Bumper', 'Toyota', 'Hilux']);
    expect(query(state())).toEqual(expect.objectContaining({
      saleType: 'FixedPrice', category: 'Body – Exterior', partType: 'Bumper', make: 'Toyota', model: 'Hilux', itemType: 'Part',
    }));
  });

  test('a chip of the same kind replaces the old one (not a second part type)', async () => {
    const { state, run } = await mountState();
    await run(b => b.applyChips([bumper]));
    await run(b => b.applyChips([headlamp]));
    expect(state().chips).toEqual([headlamp]);
    expect(query(state()).partType).toBe('Headlamp');
  });

  test('a category suggestion and a category tile are the same: category filter + chip', async () => {
    const { state, run } = await mountState();
    await run(b => b.applyChips([{ type: 'category', value: 'Lighting', label: 'Lighting' }]));
    expect(state().filters.category).toBe('Lighting');
    expect(state().search.chips).toEqual([]);
    expect(state().chips).toEqual([categoryChip('Lighting')]);
    expect(query(state()).category).toBe('Lighting');
  });

  test('pill vs chip for the same thing: the one chosen last wins (they can never disagree)', async () => {
    const { state, run } = await mountState();
    // Market's part-type pill, then a suggestion for another part type.
    await run(b => b.setFilters(f => ({ ...f, category: 'Body – Exterior', partType: 'Bonnet' })));
    await run(b => b.applyChips([bumper]));
    expect(state().filters.partType).toBeUndefined();
    expect(query(state()).partType).toBe('Bumper');

    // Now the pill again: the chip goes.
    await run(b => b.setFilters(f => ({ ...f, partType: 'Bonnet' })));
    expect(state().chips.some(c => c.type === 'partType')).toBe(false);
    expect(query(state()).partType).toBe('Bonnet');

    // A make pill drops the make / model chips from a suggestion.
    await run(b => b.applyChips([toyota, hilux]));
    await run(b => b.setFilters(f => ({ ...f, make: 'Honda' })));
    expect(query(state())).toEqual(expect.objectContaining({ make: 'Honda', model: undefined }));
  });

  test('keyboard search uses the typed text WITH the active chips and filters', async () => {
    const { state, run } = await mountState();
    await run(b => b.setSaleType('Auction'));
    await run(b => b.applyChips([bumper]));
    await run(b => b.setDraftText('front'));
    expect(query(state()).q).toBeUndefined(); // typing alone doesn't search
    await run(b => b.submitText());
    expect(query(state())).toEqual(expect.objectContaining({ q: 'front', partType: 'Bumper', saleType: 'Auction' }));
  });

  test('removing a chip and clearing update the query', async () => {
    const { state, run } = await mountState();
    await run(b => b.setSaleType('Auction'));
    await run(b => b.applyChips([categoryChip('Lighting')]));
    await run(b => b.applyChips([headlamp, toyota]));

    await run(b => b.removeChip(toyota));
    expect(query(state())).toEqual(expect.objectContaining({ make: undefined, partType: 'Headlamp', category: 'Lighting' }));

    await run(b => b.removeChip(categoryChip('Lighting')));
    expect(query(state()).category).toBeUndefined();

    // ✕: everything in the box goes; Auction stays.
    await run(b => b.applyChips([categoryChip('Lighting')]));
    await run(b => b.clearSearch());
    expect(state().chips).toEqual([]);
    expect(query(state())).toEqual(expect.objectContaining({ partType: undefined, category: undefined, saleType: 'Auction' }));

    await run(b => b.applyChips([bumper]));
    await run(b => b.clearAll());
    expect(state().hasAnything).toBe(false);
    expect(query(state())).toEqual(expect.objectContaining({ partType: undefined, saleType: undefined, itemType: 'Part' }));
  });

  test('Vehicles drops part-only chips and the category', async () => {
    const { state, run } = await mountState();
    await run(b => b.applyChips([categoryChip('Lighting')]));
    await run(b => b.applyChips([headlamp, toyota]));
    await run(b => b.setItemType('Vehicle'));
    expect(state().chips).toEqual([toyota]);
    expect(query(state())).toEqual(expect.objectContaining({ itemType: 'Vehicle', category: undefined, partType: undefined, make: 'Toyota' }));
  });

  test('a new session (logout / login, app restart) starts clean', async () => {
    const first = await mountState();
    await first.run(b => b.applyChips([bumper]));
    await ReactTestRenderer.act(async () => first.tree.unmount());

    const second = await mountState();
    expect(second.state().chips).toEqual([]);
    expect(second.state().hasAnything).toBe(false);
    await ReactTestRenderer.act(async () => second.tree.unmount());
  });
});

/**
 * THE HOME BUG: React Native gives a tap to a ScrollView first when the keyboard is open, unless EVERY ScrollView
 * around the touched view has keyboardShouldPersistTaps="handled" (or "always"). Home's ScrollView didn't have it,
 * so tapping a suggestion only closed the keyboard. fireEvent / onPress() tests can't see that – this test does.
 */
describe('suggestion taps reach the suggestion (keyboardShouldPersistTaps)', () => {
  beforeEach(() => {
    (MarketApi.search as jest.Mock).mockReset().mockResolvedValue({ results: [], totalCount: 0, pageCount: 1, chips: [], keywords: '', facets: {} });
    (MarketApi.suggest as jest.Mock).mockReset().mockResolvedValue([]);
  });

  const navigation = { navigate: jest.fn() } as any;
  const screens: [string, React.ReactElement][] = [
    ['Home', <HomeScreen navigation={navigation} route={{ key: 'h', name: 'Home' } as any} />],
    ['Market', <MarketplaceSearchScreen navigation={navigation} route={{ key: 'm', name: 'Marketplace' } as any} />],
  ];

  test.each(screens)('%s: every ScrollView around the search box lets taps through', async (_name, screen) => {
    let tree: ReactTestRenderer.ReactTestRenderer | undefined;
    await ReactTestRenderer.act(async () => {
      tree = ReactTestRenderer.create(<BrowseProvider>{screen}</BrowseProvider>);
    });
    const input = tree!.root.find(n => n.props.accessibilityLabel === 'Search' && n.props.onChangeText);

    const scrollViews: ReactTestRenderer.ReactTestInstance[] = [];
    for (let node = input.parent; node; node = node.parent) {
      if ((node.type as unknown) === 'RCTScrollView') scrollViews.push(node);
    }
    // Home has its page ScrollView (and the chip bar's own one); the check would be empty without them.
    expect(scrollViews.length).toBeGreaterThan(0);
    for (const sv of scrollViews) expect(['handled', 'always']).toContain(sv.props.keyboardShouldPersistTaps);

    await ReactTestRenderer.act(async () => tree!.unmount());
  });
});
