/** The animations the customer app can play. The keys are the server's; a new one is added there, here and in the app. */
export const CARD_ANIMATIONS = [
  { key: 'confetti', name: 'Confetti', pieces: ['🎊', '🎉', '✨'] },
  { key: 'sparkle', name: 'Sparkle', pieces: ['✨', '⭐', '✨'] },
  { key: 'gift', name: 'Gifts', pieces: ['🎁', '🎀', '✨'] },
  { key: 'balloons', name: 'Balloons', pieces: ['🎈', '🎈', '✨'] },
];

export const RIBBON_LENGTH = 20;

export const animationName = (key: string | null | undefined): string => CARD_ANIMATIONS.find(a => a.key === key)?.name ?? '';
