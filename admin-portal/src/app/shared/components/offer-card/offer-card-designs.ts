/**
 * The festival offer card designs from the QuickCart design file (Festival offer cards). The key is what the API stores on an offer; the
 * customer app draws the same eight. Colours are the design's own and pass WCAG AA for their text.
 */
export interface OfferCardDesign {
  key: string;
  name: string;
  mr: string;
  bg: string;
  ink: string;
  muted: string;
  chipBg: string;
  cta: string;
  ctaBg: string;
  ctaInk: string;
  m1: string;
  m2: string;
  m3: string;
}

const Y = '#FFD400';
const INK = '#141118';

export const OFFER_CARD_DESIGNS: OfferCardDesign[] = [
  { key: 'diwali', name: 'Diwali', mr: 'दिवाळी', bg: '#4A0E2E', ink: '#FFFFFF', muted: '#F4D9E6', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop Diwali', ctaBg: '#FFC94D', ctaInk: '#2A0718', m1: '#FFC94D', m2: '#FF8A00', m3: '#FFF1A8' },
  { key: 'ganesh', name: 'Ganeshotsav', mr: 'गणेशोत्सव', bg: '#FF8A00', ink: '#2A1405', muted: '#4A2A0A', chipBg: 'rgba(42,20,5,0.12)', cta: 'Shop puja', ctaBg: '#2A1405', ctaInk: '#FFFFFF', m1: '#FFE3B8', m2: '#C25E00', m3: '#FFFFFF' },
  { key: 'navratri', name: 'Navratri', mr: 'नवरात्री', bg: '#6A1B9A', ink: '#FFFFFF', muted: '#EBD7F5', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop now', ctaBg: Y, ctaInk: INK, m1: Y, m2: '#FF7A1A', m3: '#FFFFFF' },
  { key: 'holi', name: 'Holi', mr: 'होळी', bg: '#0B6E79', ink: '#FFFFFF', muted: '#D4F1F4', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop Holi', ctaBg: Y, ctaInk: INK, m1: Y, m2: '#FF7A1A', m3: '#B9FFCF' },
  { key: 'sankranti', name: 'Sankranti', mr: 'मकर संक्रांत', bg: '#1565C0', ink: '#FFFFFF', muted: '#D6E6FA', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop now', ctaBg: Y, ctaInk: INK, m1: Y, m2: '#FFFFFF', m3: '#FF7A1A' },
  { key: 'padwa', name: 'Gudi Padwa', mr: 'गुढीपाडवा', bg: '#0B7A3E', ink: '#FFFFFF', muted: '#D7F2E2', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop now', ctaBg: Y, ctaInk: INK, m1: Y, m2: '#FFFFFF', m3: '#FF7A1A' },
  { key: 'eid', name: 'Eid', mr: 'ईद', bg: '#0E4D45', ink: '#FFFFFF', muted: '#D3ECE8', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop Eid', ctaBg: Y, ctaInk: INK, m1: Y, m2: '#FFFFFF', m3: Y },
  { key: 'christmas', name: 'Christmas', mr: 'ख्रिसमस', bg: '#B71C1C', ink: '#FFFFFF', muted: '#FBDADA', chipBg: 'rgba(255,255,255,0.18)', cta: 'Shop now', ctaBg: '#FFFFFF', ctaInk: '#B71C1C', m1: '#FFFFFF', m2: Y, m3: Y },
];

export const OFFER_CARD_KEYS = OFFER_CARD_DESIGNS.map(d => d.key);

export const designOf = (key: string | null | undefined): OfferCardDesign | null => OFFER_CARD_DESIGNS.find(d => d.key === key) ?? null;

/** "FESTIVE OFFER · ENDS IN 3 DAYS", or "ENDS TODAY", or just "FESTIVE OFFER" when the offer has no end. */
export function chipText(endsAt: string | null | undefined, now: Date = new Date()): string {
  if (!endsAt) return 'FESTIVE OFFER';
  const left = new Date(endsAt).getTime() - now.getTime();
  if (Number.isNaN(left)) return 'FESTIVE OFFER';
  const days = Math.ceil(left / 86_400_000);
  if (days <= 0) return 'FESTIVE OFFER · ENDS TODAY';
  return days === 1 ? 'FESTIVE OFFER · ENDS IN 1 DAY' : `FESTIVE OFFER · ENDS IN ${days} DAYS`;
}
