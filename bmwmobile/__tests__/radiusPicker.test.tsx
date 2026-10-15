import React, { useState } from 'react';
import ReactTestRenderer from 'react-test-renderer';
import { RadiusPicker } from '../src/components/RadiusPicker';
import { DEFAULT_RADIUS, isValidRadius, radiusFor, savedRadius } from '../src/utils/geo';
import type { CountryCode } from '../src/types/location';

const textOf = (node: ReactTestRenderer.ReactTestInstance | string): string =>
  typeof node === 'string' ? node : node.children.map(textOf).join('');
const texts = (tree: ReactTestRenderer.ReactTestRenderer) =>
  tree.root.findAll(n => (n.type as unknown) === 'Text').map(textOf).join(' | ');

let chosen: number | null = null;
/** The picker as a screen uses it: 200 pre-selected. */
const Screen: React.FC<{ country: CountryCode }> = ({ country }) => {
  const [value, setValue] = useState<number | null>(DEFAULT_RADIUS);
  chosen = value;
  return <RadiusPicker value={value} country={country} onChange={setValue} />;
};

const render = async (country: CountryCode) => {
  let tree: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    tree = ReactTestRenderer.create(<Screen country={country} />);
  });
  return tree!;
};
const chip = (tree: ReactTestRenderer.ReactTestRenderer, label: string) =>
  tree.root.find(n => n.props.accessibilityState && n.props.onPress && textOf(n) === label);
const input = (tree: ReactTestRenderer.ReactTestRenderer) => tree.root.find(n => n.props.onChangeText && n.props.keyboardType === 'number-pad');

test('USA: quick choices in miles, 200 pre-selected', async () => {
  const tree = await render('US');
  for (const n of [25, 50, 100, 200, 500]) expect(chip(tree, `${n} mi`)).toBeTruthy();
  expect(chip(tree, '200 mi').props.accessibilityState.selected).toBe(true);
  expect(input(tree).props.value).toBe('200');
  expect(texts(tree)).toContain('Pickup-only parts within 200 mi. Parts that ship are shown from anywhere in your country.');
});

test('India: the same choices in km', async () => {
  const tree = await render('IN');
  expect(chip(tree, '25 km')).toBeTruthy();
  expect(chip(tree, '200 km').props.accessibilityState.selected).toBe(true);
  expect(texts(tree)).not.toContain(' mi');
});

test('a quick choice, then a custom value; out of range is refused', async () => {
  const tree = await render('US');
  await ReactTestRenderer.act(async () => chip(tree, '25 mi').props.onPress());
  expect(chosen).toBe(25);
  expect(input(tree).props.value).toBe('25');

  await ReactTestRenderer.act(async () => input(tree).props.onChangeText('350'));
  expect(chosen).toBe(350);
  // No quick choice is selected for a custom value.
  expect(chip(tree, '200 mi').props.accessibilityState.selected).toBe(false);

  await ReactTestRenderer.act(async () => input(tree).props.onChangeText('1200'));
  expect(chosen).toBeNull();
  expect(texts(tree)).toContain('Enter a radius from 5 to 1,000 mi.');
  await ReactTestRenderer.act(async () => input(tree).props.onChangeText('3'));
  expect(chosen).toBeNull();
  await ReactTestRenderer.act(async () => input(tree).props.onChangeText('5'));
  expect(chosen).toBe(5);
});

test('radius helpers: units by country, limits, a saved location', () => {
  expect(radiusFor(200, 'US')).toEqual({ radiusValue: 200, radiusUnit: 'mi', radiusKm: 321.87 });
  expect(radiusFor(200, 'NG')).toEqual({ radiusValue: 200, radiusUnit: 'km', radiusKm: 200 });
  expect([4, 5, 1000, 1001, 12.5].map(isValidRadius)).toEqual([false, true, true, false, false]);
  // Older servers send only km: 322 km in the USA is 200 mi.
  expect(savedRadius({ radiusKm: 322, countryCode: 'US' }).radiusValue).toBe(200);
  expect(savedRadius({ radiusKm: 321.87, radiusValue: 200, countryCode: 'US' })).toEqual({ radiusValue: 200, radiusUnit: 'mi', radiusKm: 321.87 });
});
