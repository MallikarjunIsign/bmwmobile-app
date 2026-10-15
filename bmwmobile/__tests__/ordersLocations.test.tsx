import React from 'react';
import { Alert } from 'react-native';
import ReactTestRenderer from 'react-test-renderer';
import { OrderTrackingCard } from '../src/components/OrderTrackingCard';
import { LocationsScreen } from '../src/screens/LocationsScreen';
import { AddressFormScreen } from '../src/screens/AddressFormScreen';
import { MarketApi } from '../src/services/marketApi';
import { locationText } from '../src/utils/geo';
import type { Order } from '../src/types/listing';

jest.setTimeout(20000);

const home = {
  id: 'h1', label: 'Home', placeName: 'Hyderabad', latitude: 17.4849, longitude: 78.4138, radiusKm: 60, countryCode: 'IN',
  isPrimary: true, isCurrentLocation: false, addressLine: 'Flat 402, Road No. 2', area: 'Kukatpally', landmark: 'JNTU metro', postalCode: '500072',
};
const work = {
  ...home, id: 'w1', label: 'Work', isPrimary: false, addressLine: 'Plot 9, HITEC City', area: 'Madhapur', landmark: null, postalCode: null,
  latitude: 17.4483, longitude: 78.3915,
};

const mockLocations = {
  status: 'ready',
  location: { latitude: 17.4849, longitude: 78.4138, city: 'Hyderabad', countryCode: 'IN', radiusKm: 60, radiusValue: 60, radiusUnit: 'km', updatedAt: '', addressId: 'h1', label: 'Home', area: 'Kukatpally' },
  locationOff: false,
  country: 'IN',
  near: { latitude: 17.4849, longitude: 78.4138, radiusKm: 60 },
  addresses: [home, work],
  usingCurrentLocation: false,
  switchToCurrentLocation: jest.fn(),
  chooseAddress: jest.fn(),
  saveAddress: jest.fn(),
  removeAddress: jest.fn(),
};
jest.mock('../src/location/LocationContext', () => ({ useLocations: () => mockLocations }));
jest.mock('../src/services/marketApi', () => ({
  MarketApi: { orderStep: jest.fn(), chooseDelivery: jest.fn(), shippingAddresses: jest.fn(), addShippingAddress: jest.fn(), shippingEstimate: jest.fn() },
}));
jest.mock('../src/services/locationApi', () => ({ LocationApi: { searchCities: jest.fn().mockResolvedValue([]) } }));

const orderStep = MarketApi.orderStep as jest.Mock;
const chooseDelivery = MarketApi.chooseDelivery as jest.Mock;
const shippingAddresses = MarketApi.shippingAddresses as jest.Mock;
const shippingEstimate = MarketApi.shippingEstimate as jest.Mock;
const quote = (patch: object = {}) => ({
  listingId: 'a1', pickupOffered: true, shippingOffered: true,
  shipping: { available: true, charge: 350, distance: 22, unit: 'km', currency: 'INR', sizeClass: 'Large', reason: null, placeholderRates: true, ...patch },
});

const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');
const press = async (tree: ReactTestRenderer.ReactTestRenderer, match: (t: string) => boolean) => {
  let node: ReactTestRenderer.ReactTestInstance | null = tree.root.find(n => (n.type as unknown) === 'Text' && match(textOf(n)));
  while (node && !node.props.onPress) node = node.parent;
  await ReactTestRenderer.act(async () => node!.props.onPress());
};

const steps = (labels: string[], position: number, statuses: string[]) =>
  labels.map((label, i) => ({ status: statuses[i], label, done: i <= position, current: i === position, at: i <= position ? '2026-10-11T10:30:00Z' : null, note: null }));

const aliceAddress = {
  name: 'Alice Reddy', phone: '+91 98480 12345', line1: 'Flat 4, Road 2', line2: 'Kukatpally', city: 'Hyderabad', postcode: '500072', countryCode: 'IN',
};

/** bob sold it to alice; she chose Shipping. */
const shipping: Order = {
  id: 'o1', auctionId: 'a1', role: 'seller', seller: 'bob', buyer: 'alice', otherParty: 'alice', title: '2016–2020 Toyota Hilux Front Bumper',
  finalTotal: 14100, partPrice: 14000, winningBid: 100, shippingFee: 500, total: 14600, countryCode: 'IN',
  deliveryOptions: ['Pickup', 'Shipping'], deliveryMethod: 'Shipping', deliveryChoices: [], shipTo: aliceAddress,
  deliveryAddress: 'Alice Reddy, +91 98480 12345, Flat 4, Road 2, Kukatpally, Hyderabad 500072, IN', status: 'SoldAwaitingHandover', statusLabel: 'Sold',
  tracking: steps(['Sold', 'Shipped', 'Delivered', 'Completed'], 0, ['SoldAwaitingHandover', 'Shipped', 'Delivered', 'Completed']) as Order['tracking'],
  actions: [{ status: 'Shipped', label: 'Shipped' }], createdAt: '2026-10-11T09:00:00Z', contactPhone: '+919876543210',
};

const renderCard = async (order: Order, onChange = jest.fn()) => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(
      <OrderTrackingCard order={order} money={n => `₹${n.toLocaleString('en-IN')}`} onChange={onChange} onOpenListing={jest.fn()} />,
    );
  });
  return tree!;
};

beforeEach(() => {
  orderStep.mockReset();
  chooseDelivery.mockReset();
  shippingAddresses.mockReset().mockResolvedValue([]);
  shippingEstimate.mockReset().mockResolvedValue(quote());
  Object.values(mockLocations).forEach(v => typeof v === 'function' && (v as jest.Mock).mockReset?.());
});

test('the order shows the shipping steps, the total with shipping and the address; "Track order" shows dates', async () => {
  const tree = await renderCard(shipping);
  const t = texts(tree);
  ['Sold', 'Shipped', 'Delivered', 'Completed'].forEach(s => expect(t).toContain(s));
  expect(t).toContain('Total: ₹14,600');
  expect(t).toContain('Part price ₹14,000 + winning bid ₹100 + shipping ₹500 = ₹14,600');
  expect(t).toContain('Ship to: Alice Reddy · +91 98480 12345 · Flat 4, Road 2, Kukatpally, Hyderabad 500072');

  await press(tree, x => x === 'Track order ▾');
  expect(texts(tree)).toContain('11 Oct');
  expect(texts(tree)).toContain('Hide tracking ▴');
});

test('seller marks it shipped with the courier and tracking number', async () => {
  const onChange = jest.fn();
  orderStep.mockResolvedValue({ ...shipping, status: 'Shipped', statusLabel: 'Shipped' });
  const tree = await renderCard(shipping, onChange);

  await press(tree, x => x === 'Mark as shipped');
  const modal = tree.root.findAll(n => (n.type as unknown) === 'Modal').find(m => m.props.visible)!;
  const inputs = modal.findAll(n => (n.type as unknown) === 'TextInput');
  await ReactTestRenderer.act(async () => inputs[0].props.onChangeText('Delhivery'));
  await ReactTestRenderer.act(async () => inputs[1].props.onChangeText('awb123'));
  // The popup's confirm button (the step bar also says "Shipped").
  let confirm: ReactTestRenderer.ReactTestInstance | null = modal.find(n => (n.type as unknown) === 'Text' && textOf(n) === 'Shipped');
  while (confirm && !confirm.props.onPress) confirm = confirm.parent;
  await ReactTestRenderer.act(async () => confirm!.props.onPress());

  expect(orderStep).toHaveBeenCalledWith('o1', 'Shipped', { courier: 'Delhivery', trackingNumber: 'awb123' });
  expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ status: 'Shipped' }));
});

const undecided: Order = {
  ...shipping, role: 'buyer', otherParty: 'bob', deliveryMethod: null, shipTo: null, deliveryAddress: null, total: 14100,
  deliveryChoices: ['Pickup', 'Shipping'], actions: [{ status: 'Completed', label: 'Completed' }],
  tracking: steps(['Sold', 'Completed'], 0, ['SoldAwaitingHandover', 'Completed']) as Order['tracking'],
};

test('both offered: the buyer chooses Shipping and enters a new address (saved for next time)', async () => {
  chooseDelivery.mockResolvedValue({ ...undecided, deliveryMethod: 'Shipping', deliveryChoices: [], shipTo: aliceAddress, total: 14600 });
  const onChange = jest.fn();
  const tree = await renderCard(undecided, onChange);

  expect(texts(tree)).toContain('How do you want to receive it?');
  expect(texts(tree)).toContain('📍 I will pick it up · free');
  await press(tree, x => x === '🚚 Ship it to me');
  await press(tree, x => x === 'Next: shipping address');

  // The delivery location starts at the buyer's chosen saved location (Home); the backend gives the exact charge.
  expect(shippingEstimate).toHaveBeenCalledWith('a1', 17.4849, 78.4138, 'IN');
  expect(texts(tree)).toContain('Shipping charge: ₹350 · about 22 km');

  // No saved addresses yet: the form opens; the country is fixed to the seller's.
  const sheet = tree.root.findAll(n => (n.type as unknown) === 'Modal').find(m => m.props.visible)!;
  expect(texts(tree)).toContain('The seller ships within India only.');
  expect(texts(tree)).toContain('PIN CODE');
  const inputs = sheet.findAll(n => (n.type as unknown) === 'TextInput');
  const values = [aliceAddress.name, aliceAddress.phone, aliceAddress.line1, aliceAddress.line2, aliceAddress.city, aliceAddress.postcode];
  for (const [i, v] of values.entries()) await ReactTestRenderer.act(async () => inputs[i].props.onChangeText(v));
  await press(tree, x => x === 'Ship here · ₹350');

  // The address and its coordinates – never a price.
  const where = { latitude: 17.4849, longitude: 78.4138 };
  expect(chooseDelivery).toHaveBeenCalledWith('o1', 'Shipping', { address: { ...aliceAddress, ...where }, ...where });
  expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ deliveryMethod: 'Shipping', total: 14600 }));
});

test('both offered: the buyer chooses Pickup and then sees the exact pickup address', async () => {
  chooseDelivery.mockResolvedValue({ ...undecided, deliveryMethod: 'Pickup', deliveryChoices: [], pickupAddress: 'Plot 12, Road 4, KPHB Colony' });
  const onChange = jest.fn();
  const tree = await renderCard(undecided, onChange);

  await press(tree, x => x === '📍 I will pick it up · free');
  await press(tree, x => x === 'Confirm pickup');
  expect(chooseDelivery).toHaveBeenCalledWith('o1', 'Pickup');

  const picked = await renderCard(onChange.mock.calls[0][0]);
  expect(texts(picked)).toContain('Pickup at: Plot 12, Road 4, KPHB Colony');
});

test('shipping only: the buyer picks a saved address from the address book', async () => {
  shippingAddresses.mockResolvedValue([
    { id: 's1', isDefault: true, ...aliceAddress },
    { id: 's2', isDefault: false, ...aliceAddress, name: 'Alice at work', countryCode: 'US' }, // other countries are hidden
  ]);
  chooseDelivery.mockResolvedValue({ ...undecided, deliveryMethod: 'Shipping', needsShippingAddress: false, deliveryChoices: [] });
  const tree = await renderCard({ ...undecided, deliveryOptions: ['Shipping'], deliveryMethod: 'Shipping', needsShippingAddress: true, deliveryChoices: ['Shipping'], actions: [] });

  expect(texts(tree)).toContain('🚚 This item is shipped');
  await press(tree, x => x === 'Add shipping address');
  expect(texts(tree)).toContain('Alice Reddy  · Default');
  expect(texts(tree)).not.toContain('Alice at work');

  // Another saved location → a new charge; when shipping isn't possible there, the reason shows and it can't be sent.
  shippingEstimate.mockResolvedValueOnce(quote({ available: false, charge: null, reason: 'Too far to ship: about 1,630 km (up to 1,500 km for freight items)' }));
  await press(tree, x => x === '📍 Work · Madhapur');
  expect(shippingEstimate).toHaveBeenLastCalledWith('a1', 17.4483, 78.3915, 'IN');
  expect(texts(tree)).toContain('Too far to ship');
  await press(tree, x => x === 'Ship to this address');
  expect(chooseDelivery).not.toHaveBeenCalled();

  // Back to Home: available again.
  await press(tree, x => x === '📍 Home · Kukatpally');
  await press(tree, x => x === 'Ship here · ₹350');
  expect(chooseDelivery).toHaveBeenCalledWith('o1', 'Shipping', { addressId: 's1', latitude: 17.4849, longitude: 78.4138 });
});

test('buyer confirms the delivery; the seller then just waits', async () => {
  const alert = jest.spyOn(Alert, 'alert').mockImplementation((_t, _m, buttons) => buttons?.[1]?.onPress?.());
  const buyer: Order = { ...shipping, role: 'buyer', status: 'Shipped', statusLabel: 'Shipped', actions: [{ status: 'Delivered', label: 'Delivered' }] };
  orderStep.mockResolvedValue({ ...buyer, status: 'Completed' });
  const tree = await renderCard(buyer);

  await press(tree, x => x === 'Confirm delivery');
  expect(orderStep).toHaveBeenCalledWith('o1', 'Delivered');
  alert.mockRestore();

  const seller = await renderCard({ ...shipping, status: 'Shipped', actions: [] });
  expect(texts(seller)).toContain('Waiting for the buyer to confirm (delivered)');
  const waiting = await renderCard({ ...shipping, deliveryMethod: null, actions: [] });
  expect(texts(waiting)).toContain('Waiting for the buyer to choose pickup or shipping');
});

const navigation = { goBack: jest.fn(), navigate: jest.fn() } as any;

test('My locations: current location or a saved address; choosing one uses it', async () => {
  mockLocations.chooseAddress.mockResolvedValue(undefined);
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<LocationsScreen navigation={navigation} route={{ key: 'l', name: 'Locations' } as any} />);
  });
  const t = texts(tree!);
  expect(t).toContain('Use my current location');
  expect(t).toContain('Flat 402, Road No. 2, Kukatpally, Hyderabad 500072, India');
  expect(t).toContain('IN USE');

  await press(tree!, x => x === 'Use this address');
  expect(mockLocations.chooseAddress).toHaveBeenCalledWith('w1');
  expect(navigation.goBack).toHaveBeenCalled();

  await press(tree!, x => x === '+ ADD NEW ADDRESS');
  expect(navigation.navigate).toHaveBeenCalledWith('AddressForm', {});
});

test('address form: needs the street; saves the edited address', async () => {
  mockLocations.saveAddress.mockResolvedValue(undefined);
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<AddressFormScreen navigation={navigation} route={{ key: 'a', name: 'AddressForm', params: { id: 'h1' } } as any} />);
  });
  expect(texts(tree!)).toContain('PIN CODE');

  const street = tree!.root.findAll(n => (n.type as unknown) === 'TextInput' && n.props.value === 'Flat 402, Road No. 2')[0];
  await ReactTestRenderer.act(async () => street.props.onChangeText(''));
  await press(tree!, x => x === 'SAVE CHANGES');
  expect(texts(tree!)).toContain('Enter the house / flat and street');
  expect(mockLocations.saveAddress).not.toHaveBeenCalled();

  await ReactTestRenderer.act(async () => street.props.onChangeText('Flat 501, Road No. 2'));
  await press(tree!, x => x === 'SAVE CHANGES');
  expect(mockLocations.saveAddress).toHaveBeenCalledWith(
    expect.objectContaining({ addressLine: 'Flat 501, Road No. 2', area: 'Kukatpally', placeName: 'Hyderabad', label: 'Home', makePrimary: false }),
    'h1',
  );
});

test('the location line on Home', () => {
  expect(locationText({ area: 'Kukatpally', city: 'Hyderabad', countryCode: 'IN' })).toBe('Kukatpally, Hyderabad, India');
  expect(locationText({ city: 'New York', countryCode: 'US' })).toBe('New York, USA');
  expect(locationText(null)).toBeNull();
});
