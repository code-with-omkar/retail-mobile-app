/** Short help in English and Marathi, shown together. Keys are the screen's address, or "screen.control". */
export const TIPS: Record<string, { en: string; mr: string }> = {
  dashboard: { en: 'Totals for the whole business and the orders being handled now.', mr: 'संपूर्ण व्यवसायाचे एकूण आकडे आणि सध्या हाताळल्या जाणाऱ्या ऑर्डर.' },
  complaints: { en: 'Problems customers report after delivery, to approve or refuse.', mr: 'डिलिव्हरीनंतर ग्राहकांनी केलेल्या तक्रारी, मान्य किंवा अमान्य करण्यासाठी.' },
  notifications: { en: 'Coupons, offers and ads that end soon or have just ended.', mr: 'लवकरच संपणारे किंवा नुकतेच संपलेले कूपन, ऑफर आणि जाहिराती.' },
  today: { en: 'What this store has to do today.', mr: 'या दुकानाला आज करायची कामे.' },
  orders: { en: 'Accept, pack and hand over orders. The order you act on stays in its row.', mr: 'ऑर्डर स्वीकारा, पॅक करा आणि पुढे द्या. तुम्ही काम करत असलेली ऑर्डर त्याच ओळीत राहते.' },
  approvals: { en: 'Requests waiting for a yes or no.', mr: 'मंजुरीची वाट पाहणाऱ्या विनंत्या.' },
  products: { en: 'Add and edit products, their sizes, prices and pictures.', mr: 'उत्पादने, त्यांचे आकार, किंमती आणि चित्रे जोडा व बदला.' },
  categories: { en: 'Arrange categories and sub-categories shown in the app.', mr: 'अॅपमध्ये दिसणारे विभाग आणि उपविभाग लावा.' },
  stock: { en: 'Choose what a store sells and how many it has.', mr: 'दुकान काय विकते आणि किती साठा आहे ते ठरवा.' },
  stores: { en: 'Your shops, where they are and how far they deliver.', mr: 'तुमची दुकाने, त्यांचा पत्ता आणि ती किती दूर पोहोचवतात.' },
  campaigns: { en: 'Send offers to customers and give discounts.', mr: 'ग्राहकांना ऑफर पाठवा आणि सवलत द्या.' },
  ads: { en: 'Pictures shown in the customer app, with views and taps.', mr: 'ग्राहक अॅपमध्ये दिसणाऱ्या जाहिराती, किती वेळा पाहिल्या व दाबल्या.' },
  coupons: { en: 'Codes that give customers money off.', mr: 'ग्राहकांना सवलत देणारे कूपन कोड.' },
  placement: { en: 'Bid to show your store first to nearby customers.', mr: 'जवळच्या ग्राहकांना तुमचे दुकान आधी दिसण्यासाठी बोली लावा.' },
  reports: { en: 'Sales and ratings for the dates you choose.', mr: 'निवडलेल्या तारखांची विक्री आणि रेटिंग.' },
  behaviour: { en: 'How customers move through the app.', mr: 'ग्राहक अॅप कसे वापरतात.' },
  'delivery-times': { en: 'The delivery time windows customers can pick.', mr: 'ग्राहक निवडू शकतील अशा डिलिव्हरीच्या वेळा.' },
  commission: { en: 'The share taken from each store.', mr: 'प्रत्येक दुकानाकडून घेतला जाणारा वाटा.' },
  'admin/users': { en: 'People who use the system, their role and stores.', mr: 'प्रणाली वापरणाऱ्या व्यक्ती, त्यांची भूमिका आणि दुकाने.' },
  'admin/roles': { en: 'What each role is allowed to do.', mr: 'प्रत्येक भूमिकेला काय करण्याची परवानगी आहे.' },
  'admin/permissions': { en: 'The list of actions that can be allowed.', mr: 'परवानगी देता येणाऱ्या कृतींची यादी.' },
  profile: { en: 'Your account details.', mr: 'तुमच्या खात्याची माहिती.' },
  'orders.search': { en: 'Type an order number or a customer name, then press Enter.', mr: 'ऑर्डर क्रमांक किंवा ग्राहकाचे नाव लिहा आणि Enter दाबा.' },
  'orders.status': { en: 'Show only orders at one step.', mr: 'एका टप्प्यावरील ऑर्डरच दाखवा.' },
  'store.choice': { en: 'The store whose figures you are seeing.', mr: 'ज्या दुकानाची माहिती तुम्ही पाहत आहात ते दुकान.' },
};

/** The tip for a page address such as "/admin/users?x=1". */
export function tipForUrl(url: string): string | null {
  const path = url.split('?')[0].replace(/^\//, '').replace(/^admin\/dashboard$/, 'dashboard');
  return TIPS[path] ? path : null;
}
