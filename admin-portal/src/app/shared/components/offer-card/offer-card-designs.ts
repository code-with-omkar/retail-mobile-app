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
  /** A festival design. The others are everyday designs (the chip says SPECIAL OFFER instead of FESTIVE OFFER). */
  festive: boolean;
}

const Y = '#FFD400';
const INK = '#141118';

export const OFFER_CARD_DESIGNS: OfferCardDesign[] = [
  { key: 'diwali', name: 'Diwali', mr: 'दिवाळी', bg: '#4A0E2E', ink: '#FFFFFF', muted: '#F4D9E6', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop Diwali', ctaBg: '#FFC94D', ctaInk: '#2A0718', m1: '#FFC94D', m2: '#FF8A00', m3: '#FFF1A8', festive: true },
  { key: 'ganesh', name: 'Ganeshotsav', mr: 'गणेशोत्सव', bg: '#FF8A00', ink: '#2A1405', muted: '#4A2A0A', chipBg: 'rgba(42,20,5,0.12)', cta: 'Shop puja', ctaBg: '#2A1405', ctaInk: '#FFFFFF', m1: '#FFE3B8', m2: '#C25E00', m3: '#FFFFFF', festive: true },
  { key: 'navratri', name: 'Navratri', mr: 'नवरात्री', bg: '#6A1B9A', ink: '#FFFFFF', muted: '#EBD7F5', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop now', ctaBg: Y, ctaInk: INK, m1: Y, m2: '#FF7A1A', m3: '#FFFFFF', festive: true },
  { key: 'holi', name: 'Holi', mr: 'होळी', bg: '#0B6E79', ink: '#FFFFFF', muted: '#D4F1F4', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop Holi', ctaBg: Y, ctaInk: INK, m1: Y, m2: '#FF7A1A', m3: '#B9FFCF', festive: true },
  { key: 'sankranti', name: 'Sankranti', mr: 'मकर संक्रांत', bg: '#1565C0', ink: '#FFFFFF', muted: '#D6E6FA', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop now', ctaBg: Y, ctaInk: INK, m1: Y, m2: '#FFFFFF', m3: '#FF7A1A', festive: true },
  { key: 'padwa', name: 'Gudi Padwa', mr: 'गुढीपाडवा', bg: '#0B7A3E', ink: '#FFFFFF', muted: '#D7F2E2', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop now', ctaBg: Y, ctaInk: INK, m1: Y, m2: '#FFFFFF', m3: '#FF7A1A', festive: true },
  { key: 'eid', name: 'Eid', mr: 'ईद', bg: '#0E4D45', ink: '#FFFFFF', muted: '#D3ECE8', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop Eid', ctaBg: Y, ctaInk: INK, m1: Y, m2: '#FFFFFF', m3: Y, festive: true },
  { key: 'christmas', name: 'Christmas', mr: 'ख्रिसमस', bg: '#B71C1C', ink: '#FFFFFF', muted: '#FBDADA', chipBg: 'rgba(255,255,255,0.18)', cta: 'Shop now', ctaBg: '#FFFFFF', ctaInk: '#B71C1C', m1: '#FFFFFF', m2: Y, m3: Y, festive: true },
  { key: 'fresh', name: 'Fresh picks', mr: 'ताजे निवडक', bg: '#0F6B3A', ink: '#FFFFFF', muted: '#D9F2E3', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop fresh', ctaBg: Y, ctaInk: INK, m1: '#B9F6CA', m2: Y, m3: '#FFFFFF', festive: false },
  { key: 'weekend', name: 'Weekend sale', mr: 'वीकेंड सेल', bg: '#3730A3', ink: '#FFFFFF', muted: '#E0E0FF', chipBg: 'rgba(255,255,255,0.16)', cta: 'See the sale', ctaBg: Y, ctaInk: INK, m1: Y, m2: '#FFFFFF', m3: '#A5B4FC', festive: false },
  { key: 'bigsaver', name: 'Big saver', mr: 'मोठी बचत', bg: '#C42F1D', ink: '#FFFFFF', muted: '#FFE8E4', chipBg: 'rgba(255,255,255,0.16)', cta: 'Save now', ctaBg: '#FFFFFF', ctaInk: '#C42F1D', m1: Y, m2: '#FFFFFF', m3: '#FFB4A6', festive: false },
  { key: 'combo', name: 'Combo deal', mr: 'कॉम्बो ऑफर', bg: '#0E7490', ink: '#FFFFFF', muted: '#E3F8FC', chipBg: 'rgba(255,255,255,0.16)', cta: 'Shop combos', ctaBg: Y, ctaInk: INK, m1: Y, m2: '#A5F3FC', m3: '#FFFFFF', festive: false },
  { key: 'freeship', name: 'Free delivery', mr: 'मोफत डिलिव्हरी', bg: '#141118', ink: '#FFFFFF', muted: '#D7D4E0', chipBg: 'rgba(255,255,255,0.16)', cta: 'Order now', ctaBg: Y, ctaInk: INK, m1: Y, m2: '#FFFFFF', m3: '#62606E', festive: false },
  { key: 'newin', name: 'New in store', mr: 'नवीन आले', bg: '#9D174D', ink: '#FFFFFF', muted: '#FBD5E5', chipBg: 'rgba(255,255,255,0.16)', cta: 'See what is new', ctaBg: '#FFFFFF', ctaInk: '#9D174D', m1: Y, m2: '#FFFFFF', m3: '#F9A8D4', festive: false },
];

export const OFFER_CARD_KEYS = OFFER_CARD_DESIGNS.map(d => d.key);

export const designOf = (key: string | null | undefined): OfferCardDesign | null => OFFER_CARD_DESIGNS.find(d => d.key === key) ?? null;

/** "FESTIVE OFFER · ENDS IN 3 DAYS", or "ENDS TODAY", or just "FESTIVE OFFER" when the offer has no end; "SPECIAL OFFER" for an everyday design. */
export function chipText(endsAt: string | null | undefined, now: Date = new Date(), festive = true): string {
  const base = festive ? 'FESTIVE OFFER' : 'SPECIAL OFFER';
  if (!endsAt) return base;
  const left = new Date(endsAt).getTime() - now.getTime();
  if (Number.isNaN(left)) return base;
  const days = Math.ceil(left / 86_400_000);
  if (days <= 0) return `${base} · ENDS TODAY`;
  return days === 1 ? `${base} · ENDS IN 1 DAY` : `${base} · ENDS IN ${days} DAYS`;
}
