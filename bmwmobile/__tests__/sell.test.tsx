import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import { SellWizardScreen } from '../src/screens/sell/SellWizardScreen';
import { MarketApi } from '../src/services/marketApi';
import { ServerError } from '../src/services/api';
import { buildQuery } from '../src/hooks/useListingSearch';
import { notificationText } from '../src/components/NotificationToasts';
import { formatMoney } from '../src/utils/geo';

jest.setTimeout(20000);

const mockLocations = {
  status: 'ready',
  location: { latitude: 17.4849, longitude: 78.4138, city: 'Hyderabad', countryCode: 'IN', radiusKm: 60, radiusValue: 60, radiusUnit: 'km', updatedAt: '' },
  locationOff: false,
  country: 'IN',
  near: { latitude: 17.4849, longitude: 78.4138, radiusKm: 60 },
};
jest.mock('../src/location/LocationContext', () => ({ useLocations: () => mockLocations }));
jest.mock('../src/services/marketApi', () => ({
  MarketApi: {
    getListing: jest.fn(),
    partType: jest.fn(),
    bidSteps: jest.fn(),
    saveListing: jest.fn(),
    publishListing: jest.fn(),
    lastFitment: jest.fn(),
  },
}));

const api = MarketApi as unknown as Record<string, jest.Mock>;

const draft = {
  id: 'd1', status: 'Draft', seller: 'bob', kind: 'VehicleSpecificPart', itemType: 'Part', partTypeId: 7, partType: 'Bumper',
  title: 'Front Bumper', description: '', make: '', model: '', year: 0, deliveryOptions: ['Pickup'], startingPrice: 1, bidCount: 0,
  auctionEnd: '', fitments: [], partNumbers: [], partNumberItems: [], specs: {}, imageItems: [], imageUrl: '', canEdit: true,
  countryCode: 'IN', currency: 'INR', locationName: 'Kukatpally, Hyderabad', latitude: 17.4849, longitude: 78.4138, durationDays: 7,
  partDetails: {},
};

const bumper = {
  defaultSizeClass: 'Large',
  id: 7, name: 'Bumper', categoryId: 1, category: 'Body – Exterior', fitmentMode: 'VehicleSpecific', hasPosition: true, hasSide: false,
  isBodyPart: true, synonyms: [], importantFitmentFields: [], specFields: [],
};

const navigation = { goBack: jest.fn(), replace: jest.fn(), navigate: jest.fn() } as any;

const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');
const press = async (tree: ReactTestRenderer.ReactTestRenderer, match: (t: string) => boolean) => {
  let node: ReactTestRenderer.ReactTestInstance | null = tree.root.find(n => (n.type as unknown) === 'Text' && match(textOf(n)));
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
};

const mounted: ReactTestRenderer.ReactTestRenderer[] = [];
const render = async (step?: string) => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<SellWizardScreen navigation={navigation} route={{ key: 's', name: 'SellWizard', params: { listingId: 'd1', step } } as any} />);
  });
  mounted.push(tree!);
  return tree!;
};

// Unmount, so the live countdowns stop before the test environment is torn down.
afterEach(async () => {
  for (const tree of mounted.splice(0)) await ReactTestRenderer.act(async () => tree.unmount());
});

beforeEach(() => {
  Object.values(api).forEach(m => m.mockReset());
  api.getListing.mockResolvedValue(draft);
  api.partType.mockResolvedValue(bumper);
  api.bidSteps.mockResolvedValue([{ from: 0, to: 999, step: 10 }, { from: 1000, to: null, step: 20 }]);
  api.saveListing.mockImplementation(async (_id: string, body: any) => ({ ...draft, ...body, partNumberItems: body.partNumbers ?? [] }));
  navigation.replace.mockReset();
});

test('vehicle-specific part: part type → fitment → details → photos → location → auction → review', async () => {
  const tree = await render();
  const t = texts(tree);

  expect(t).toContain('Step 1 of 7: Part type');
  expect(t).toContain('Bumper');
  expect(t).toContain('Next: Fits which vehicles');

  await press(tree, x => x === 'Next: Fits which vehicles');
  expect(texts(tree)).toContain('Which vehicles does it fit?');
  expect(texts(tree)).toContain('⧉ Copy fitment from my last listing');

  await press(tree, x => x.startsWith('+ Add'));
  expect(texts(tree)).toContain('Vehicle 1');
  expect(texts(tree)).toContain('YEAR FROM');
});

test('a common part asks no vehicle questions; size-based parts ask only for the size', async () => {
  api.getListing.mockResolvedValue({ ...draft, kind: 'CommonPart', partTypeId: 9, partType: 'Tyre' });
  api.partType.mockResolvedValue({
    ...bumper, id: 9, name: 'Tyre', fitmentMode: 'SizeBased', hasPosition: false,
    specFields: [
      { key: 'width', label: 'Width', type: 'number', unit: 'mm', options: [], required: true },
      { key: 'profile', label: 'Profile', type: 'number', options: [], required: true },
      { key: 'rim', label: 'Rim size', type: 'number', unit: 'in', options: [], required: true },
    ],
  });
  const tree = await render();
  await ReactTestRenderer.act(async () => new Promise(r => setTimeout(r, 0)));

  expect(texts(tree)).toContain('Next: Size / spec');
  await press(tree, x => x === 'Next: Size / spec');
  const t = texts(tree);
  expect(t).toContain('WIDTH');
  expect(t).toContain('RIM SIZE');
  expect(t).not.toContain('Which vehicles does it fit?');
});

test('vehicles: identity, condition, papers by country, 8 guided photos', async () => {
  api.getListing.mockResolvedValue({ ...draft, kind: 'Vehicle', itemType: 'Vehicle', partTypeId: null, vehicle: { damageAreas: [], inspectionAllowed: false, papers: {} } });
  const tree = await render();

  expect(texts(tree)).toContain('Step 1 of 7: Vehicle');
  await press(tree, x => x === 'Next: Condition');
  await press(tree, x => x === 'Next: Papers');
  const t = texts(tree);
  expect(t).toContain('Papers (India)');
  expect(t).toContain('RC (REGISTRATION CERTIFICATE) AVAILABLE');
  await press(tree, x => x === 'Next: Photos');
  expect(texts(tree)).toContain('Engine bay');
  expect(texts(tree)).toContain('0 / 20 photos – add at least 8 more');
});

test('auction step shows the bid steps read-only (not a seller setting) and the part price in the local currency', async () => {
  const tree = await render('auction');
  const t = texts(tree);

  expect(t).toContain('PART PRICE (INR)');
  expect(t).toContain('HOW BIDS GO UP (SET BY SHOPCARPARTS)');
  expect(t).toContain('+ ₹10');
  expect(t).toContain('No reserve price');
});

test('delivery: tick Pickup and / or Shipping; Shipping has no fee to enter – only the size class (part type default) and the country', async () => {
  const tree = await render('location');
  let t = texts(tree);
  expect(t).toContain('HOW CAN THE BUYER GET IT? (AT LEAST ONE)');
  expect(t).not.toContain('Buyer arranges');
  expect(t).toContain('EXACT PICKUP ADDRESS');

  await press(tree, x => x === 'Shipping');
  t = texts(tree);
  expect(t).not.toContain('SHIPPING FEE');
  expect(t).not.toContain('Free shipping');
  expect(t).toContain("No fee to enter: each buyer's shipping charge is worked out from the size class and the distance to them.");
  expect(t).toContain('SIZE CLASS');
  expect(t).toContain('SHIPS WITHIN');
  expect(t).toContain('India');
  // The bumper's default size class is preselected.
  expect(t).toContain('Large – bumper, door, bonnet');
  const selected = tree.root.findAll(n => n.props.accessibilityState?.selected === true && n.props.onPress).map(n => textOf(n));
  expect(selected).toContain('Large');

  // The seller can change it.
  await press(tree, x => x === 'Freight');
  await ReactTestRenderer.act(async () => new Promise(r => setTimeout(r, 1300)));
  expect(api.saveListing).toHaveBeenLastCalledWith('d1', expect.objectContaining({ deliveryOptions: ['Pickup', 'Shipping'], sizeClass: 'Freight' }));
  expect(api.saveListing.mock.calls.at(-1)![1]).not.toHaveProperty('shippingFee', expect.any(Number));

  // Unticking Pickup leaves Shipping only (no pickup address).
  await press(tree, x => x === 'Pickup');
  expect(texts(tree)).not.toContain('EXACT PICKUP ADDRESS');
});

test('US listing: Shipping asks for the package (pre-filled for the part type, editable) for the USPS price', async () => {
  api.getListing.mockResolvedValue({
    ...draft, countryCode: 'US', currency: 'USD', locationName: 'Manhattan, New York', latitude: 40.7128, longitude: -74.006,
    deliveryOptions: ['Pickup', 'Shipping'],
  });
  api.partType.mockResolvedValue({ ...bumper, defaultWeightLb: 25, defaultLengthIn: 72, defaultWidthIn: 20, defaultHeightIn: 14 });
  const tree = await render('location');

  const t = texts(tree);
  expect(t).toContain('PACKAGE (FOR THE USPS PRICE)');
  expect(t).toContain('USPS Ground Advantage retail');
  const input = (label: string) => tree.root.findAll(n => n.props.accessibilityLabel === label && n.props.onChangeText)[0];
  expect(input('Package weight in pounds').props.value).toBe('25');
  expect(input('Box length in inches').props.value).toBe('72');

  // The seller packs it smaller and lighter.
  await ReactTestRenderer.act(async () => input('Package weight in pounds').props.onChangeText('18.5'));
  await ReactTestRenderer.act(async () => input('Box length in inches').props.onChangeText('60'));
  await ReactTestRenderer.act(async () => new Promise(r => setTimeout(r, 1300)));
  expect(api.saveListing).toHaveBeenLastCalledWith('d1', expect.objectContaining({ weightLb: 18.5, lengthIn: 60 }));
});

test('non-US listing: no package fields (other countries keep the distance-based charge)', async () => {
  api.getListing.mockResolvedValue({ ...draft, deliveryOptions: ['Pickup', 'Shipping'] });
  const tree = await render('location');
  expect(texts(tree)).not.toContain('PACKAGE (FOR THE USPS PRICE)');
});

test('the seller picks the date and time the auction ends (sent as UTC, replacing the older duration in days)', async () => {
  const tree = await render('auction');
  expect(texts(tree)).toContain('AUCTION ENDS');

  // The field shows the planned end ("Mon 19 Oct, 11:45 am"); tapping it opens the picker.
  await press(tree, x => /^\w{3} \d{1,2} \w{3}, \d{1,2}:\d{2} (am|pm)$/.test(x));
  await press(tree, x => x === 'In 3 days');
  await press(tree, x => x === 'Set end time');
  await ReactTestRenderer.act(async () => new Promise(r => setTimeout(r, 1300)));

  const body = api.saveListing.mock.calls.at(-1)![1];
  expect(body.durationDays).toBeUndefined();
  const hours = (new Date(body.auctionEnd).getTime() - Date.now()) / 3600000;
  expect(hours).toBeGreaterThan(71.9);
  expect(hours).toBeLessThan(72.3);
});

test('direct sale: no end time or bid steps, the seller sets the units for sale', async () => {
  const tree = await render('auction');
  const options = texts(tree);
  expect(options).toContain('| Auction |');
  expect(options).toContain('| Direct sale |');
  await press(tree, x => x === 'Direct sale');

  const t = texts(tree);
  expect(t).toContain('It stays listed until it is sold out or you cancel it.');
  expect(t).toContain('UNITS FOR SALE');
  expect(t).not.toContain('AUCTION ENDS');
  expect(t).not.toContain('HOW BIDS GO UP');

  await ReactTestRenderer.act(async () => new Promise(r => setTimeout(r, 1300)));
  expect(api.saveListing).toHaveBeenCalledWith('d1', expect.objectContaining({ saleType: 'FixedPrice' }));
});

test('changes are saved automatically; publish errors point to the step to fix', async () => {
  const tree = await render('details');
  await press(tree, x => x === 'Genuine OEM');
  await ReactTestRenderer.act(async () => new Promise(r => setTimeout(r, 1300)));
  expect(api.saveListing).toHaveBeenCalledWith('d1', expect.objectContaining({ partDetails: expect.objectContaining({ brandType: 'GenuineOem' }) }));

  api.publishListing.mockRejectedValue(new ServerError('Invalid', 400, { fitments: ['Add at least one vehicle this part fits'], photos: ['Add at least 3 photos (0 so far)'] }));
  const review = await render('review');
  await press(review, x => x === 'Publish listing');
  const t = texts(review);
  expect(t).toContain('Please fix these first');
  expect(t).toContain('Fits which vehicles: Add at least one vehicle this part fits');
  expect(t).toContain('Photos: Add at least 3 photos (0 so far)');
  expect(navigation.replace).not.toHaveBeenCalled();
});

test('publishing a complete listing opens it', async () => {
  api.publishListing.mockResolvedValue({ ...draft, status: 'Live', title: '2016–2020 Toyota Hilux Front Bumper' });
  const tree = await render('review');

  await press(tree, x => x === 'Publish listing');

  expect(navigation.replace).toHaveBeenCalledWith('PartDetails', { auctionId: 'd1' });
});

test('search query from chips, text, filters and the garage vehicle', () => {
  const q = buildQuery(
    {
      text: 'with fog lamp holes',
      chips: [
        { type: 'partType', value: 'Bumper', label: 'Front bumper' },
        { type: 'make', value: 'Toyota', label: 'Toyota' },
        { type: 'model', value: 'Hilux', label: 'Hilux' },
        { type: 'year', value: '2016', label: '2016' },
      ],
      filters: { sort: 'priceAsc', condition: 'Used' },
    },
    null,
  );
  expect(q).toEqual(expect.objectContaining({ partType: 'Bumper', make: 'Toyota', model: 'Hilux', year: 2016, q: 'front with fog lamp holes', sort: 'priceAsc', condition: 'Used' }));

  const garage = buildQuery({ text: 'bumper', chips: [], filters: {} }, { id: 'g', year: 2018, make: 'Toyota', model: 'Hilux' });
  expect(garage).toEqual(expect.objectContaining({ make: 'Toyota', model: 'Hilux', year: 2018, q: 'bumper' }));
});

test('notification texts', () => {
  const money = (n: number) => formatMoney(n, 'US');
  expect(notificationText({ type: 'outbid', auctionId: 'a', title: 'Hilux bumper', amount: 340 }, money).title).toBe("You've been outbid");
  expect(notificationText({ type: 'won', auctionId: 'a', title: 'Hilux bumper', amount: 340 }, money).body).toContain('$340');
  expect(notificationText({ type: 'endingSoon', auctionId: 'a', title: 'X' }, money).title).toBe('Auction ending in 1 hour');
  expect(notificationText({ type: 'endedNoBids', auctionId: 'a', title: 'X' }, money).title).toBe('Ended – no bids');
});
