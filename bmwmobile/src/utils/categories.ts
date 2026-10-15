/** "WheelsAndTyres" → "Wheels & Tyres" (older listings store the enum name); master-data names stay as they are. */
export const categoryLabel = (value: string) =>
  value.replace(/And/g, ' & ').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/\s+/g, ' ').trim();

const ICONS: [RegExp, string][] = [
  [/brake/i, '🛑'],
  [/light|lamp|bulb/i, '💡'],
  [/wheel|tyre|tire/i, '🛞'],
  [/engine|motor/i, '⚙️'],
  [/transmission|gear|clutch|drive/i, '🔩'],
  [/suspension|steering/i, '🔧'],
  [/electric|batter|sensor|ecu/i, '🔌'],
  [/cool|radiator|air ?con|a\/c|heating/i, '❄️'],
  [/exhaust|silencer|muffler/i, '💨'],
  [/body|bumper|door|panel|exterior/i, '🚗'],
  [/interior|seat|dash/i, '💺'],
  [/glass|mirror|window|wiper/i, '🪟'],
  [/fuel/i, '⛽'],
  [/oil|fluid|filter|service/i, '🛢️'],
  [/accessor|tool/i, '🧰'],
];

/** A simple icon for a category tile. */
export const categoryIcon = (value: string) => ICONS.find(([re]) => re.test(value))?.[1] ?? '🔧';
