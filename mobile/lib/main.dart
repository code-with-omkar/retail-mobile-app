import 'package:flutter/material.dart';

void main() => runApp(const QuickCartApp());

class QuickCartApp extends StatelessWidget {
  const QuickCartApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(title: 'QuickCart', debugShowCheckedModeBanner: false, theme: ThemeData(colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF86A94B)), scaffoldBackgroundColor: const Color(0xFFF7F8F4), fontFamily: 'Arial', useMaterial3: true), home: const HomeScreen());
}

class Product {
  const Product(this.name, this.unit, this.price, this.image, this.color);
  final String name;
  final String unit;
  final int price;
  final String image;
  final Color color;
}

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});
  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  static const products = [
    Product('Tomato', '1 kg', 45, '🍅', Color(0xFFFFE4D9)),
    Product('Farm Milk', '500 ml', 34, '🥛', Color(0xFFE3F1F5)),
    Product('Royal Apples', '1 kg', 149, '🍎', Color(0xFFFFEBD5)),
    Product('Daily Rice', '1 kg', 89, '🍚', Color(0xFFF0EBDD)),
  ];
  final quantities = <String, int>{};
  String search = '';
  int tab = 0;
  int get cartCount => quantities.values.fold(0, (sum, quantity) => sum + quantity);

  @override
  Widget build(BuildContext context) {
    final visibleProducts = products.where((product) => product.name.toLowerCase().contains(search.toLowerCase())).toList();
    return Scaffold(
      body: SafeArea(child: CustomScrollView(slivers: [
        SliverPadding(padding: const EdgeInsets.fromLTRB(20, 22, 20, 8), sliver: SliverToBoxAdapter(child: _header())),
        SliverPadding(padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12), sliver: SliverToBoxAdapter(child: _searchBar())),
        SliverPadding(padding: const EdgeInsets.fromLTRB(20, 4, 20, 16), sliver: SliverToBoxAdapter(child: _hero())),
        SliverPadding(padding: const EdgeInsets.only(left: 20, bottom: 14), sliver: SliverToBoxAdapter(child: _categories())),
        SliverPadding(padding: const EdgeInsets.symmetric(horizontal: 20), sliver: SliverToBoxAdapter(child: _sectionTitle())),
        SliverPadding(padding: const EdgeInsets.fromLTRB(20, 12, 20, 28), sliver: SliverGrid.builder(gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 2, mainAxisSpacing: 14, crossAxisSpacing: 14, childAspectRatio: .70), itemCount: visibleProducts.length, itemBuilder: (_, index) => _productCard(visibleProducts[index]))),
      ])),
      bottomNavigationBar: NavigationBar(selectedIndex: tab, onDestinationSelected: (value) => setState(() => tab = value), destinations: [
        const NavigationDestination(icon: Icon(Icons.home_outlined), selectedIcon: Icon(Icons.home), label: 'Home'),
        NavigationDestination(icon: Badge(isLabelVisible: cartCount > 0, label: Text('$cartCount'), child: const Icon(Icons.shopping_bag_outlined)), selectedIcon: const Icon(Icons.shopping_bag), label: 'Cart'),
        const NavigationDestination(icon: Icon(Icons.receipt_long_outlined), label: 'Orders'),
        const NavigationDestination(icon: Icon(Icons.person_outline), label: 'Profile'),
      ]),
    );
  }

  Widget _header() => Row(children: [
        Container(width: 39, height: 39, alignment: Alignment.center, decoration: BoxDecoration(color: const Color(0xFF1E312C), borderRadius: BorderRadius.circular(12)), child: const Text('Q', style: TextStyle(color: Color(0xFFE1F39A), fontSize: 22, fontWeight: FontWeight.bold))),
        const SizedBox(width: 11),
        const Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [Text('DELIVERING TO', style: TextStyle(fontSize: 10, letterSpacing: 1.3, color: Color(0xFF8A9690), fontWeight: FontWeight.bold)), SizedBox(height: 3), Row(children: [Text('Home · Bandra West', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 14)), Icon(Icons.keyboard_arrow_down, size: 18)])])),
        IconButton(onPressed: () {}, icon: const Icon(Icons.notifications_none_rounded)),
      ]);

  Widget _searchBar() => TextField(onChanged: (value) => setState(() => search = value), decoration: InputDecoration(prefixIcon: const Icon(Icons.search), hintText: 'Search for groceries...', filled: true, fillColor: Colors.white, border: OutlineInputBorder(borderSide: BorderSide.none, borderRadius: BorderRadius.circular(14)), contentPadding: const EdgeInsets.symmetric(vertical: 14)));

  Widget _hero() => Container(padding: const EdgeInsets.all(20), decoration: BoxDecoration(color: const Color(0xFF1E312C), borderRadius: BorderRadius.circular(18)), child: Row(children: [const Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [Text('Freshness\nat your doorstep.', style: TextStyle(color: Colors.white, fontSize: 22, fontWeight: FontWeight.bold, height: 1.1)), SizedBox(height: 9), Text('Handpicked daily from your\nnearest local store.', style: TextStyle(color: Color(0xFFB4C2BA), fontSize: 12)), SizedBox(height: 15), Text('Shop fresh  →', style: TextStyle(color: Color(0xFFDDF38F), fontWeight: FontWeight.bold, fontSize: 12))])), Container(width: 90, height: 90, alignment: Alignment.center, decoration: BoxDecoration(color: const Color(0xFFDDF38F), borderRadius: BorderRadius.circular(45)), child: const Text('🥬', style: TextStyle(fontSize: 48)))]));

  Widget _categories() => SizedBox(height: 82, child: ListView(scrollDirection: Axis.horizontal, children: const [CategoryTile('All', '✦', true), CategoryTile('Vegetables', '🥕', false), CategoryTile('Fruits', '🍏', false), CategoryTile('Dairy', '🥛', false), CategoryTile('Snacks', '🍪', false)]));
  Widget _sectionTitle() => const Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [Text('Popular near you', style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold)), Text('See all  →', style: TextStyle(color: Color(0xFF789B3D), fontSize: 12, fontWeight: FontWeight.bold))]);

  Widget _productCard(Product product) {
    final quantity = quantities[product.name] ?? 0;
    return Container(padding: const EdgeInsets.all(10), decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(15), border: Border.all(color: const Color(0xFFE7EBE4))), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [Expanded(child: Container(width: double.infinity, alignment: Alignment.center, decoration: BoxDecoration(color: product.color, borderRadius: BorderRadius.circular(11)), child: Text(product.image, style: const TextStyle(fontSize: 55)))), const SizedBox(height: 10), Text(product.name, style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14)), const SizedBox(height: 3), Text(product.unit, style: const TextStyle(color: Color(0xFF8C9891), fontSize: 11)), const SizedBox(height: 9), Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [Text('₹${product.price}', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 15)), quantity == 0 ? IconButton(onPressed: () => _changeQuantity(product, 1), icon: const Icon(Icons.add_circle, color: Color(0xFF789B3D), size: 28), padding: EdgeInsets.zero, constraints: const BoxConstraints()) : _quantityControl(product, quantity)])]));
  }

  Widget _quantityControl(Product product, int quantity) => Container(height: 29, decoration: BoxDecoration(color: const Color(0xFFE7F2D1), borderRadius: BorderRadius.circular(8)), child: Row(children: [IconButton(onPressed: () => _changeQuantity(product, -1), icon: const Icon(Icons.remove, size: 14), padding: EdgeInsets.zero, constraints: const BoxConstraints(minWidth: 28)), Text('$quantity', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 12)), IconButton(onPressed: () => _changeQuantity(product, 1), icon: const Icon(Icons.add, size: 14), padding: EdgeInsets.zero, constraints: const BoxConstraints(minWidth: 28))]));
  void _changeQuantity(Product product, int change) => setState(() { final next = (quantities[product.name] ?? 0) + change; if (next <= 0) { quantities.remove(product.name); } else { quantities[product.name] = next; } });
}

class CategoryTile extends StatelessWidget {
  const CategoryTile(this.label, this.emoji, this.selected, {super.key});
  final String label;
  final String emoji;
  final bool selected;
  @override
  Widget build(BuildContext context) => Container(width: 76, margin: const EdgeInsets.only(right: 12), child: Column(children: [Container(width: 56, height: 56, alignment: Alignment.center, decoration: BoxDecoration(color: selected ? const Color(0xFFE7F2D1) : Colors.white, borderRadius: BorderRadius.circular(17), border: Border.all(color: selected ? const Color(0xFFB9D77C) : const Color(0xFFE7EBE4), width: selected ? 1.5 : 1)), child: Text(emoji, style: const TextStyle(fontSize: 25))), const SizedBox(height: 7), Text(label, overflow: TextOverflow.ellipsis, style: TextStyle(fontSize: 11, fontWeight: selected ? FontWeight.bold : FontWeight.normal, color: selected ? const Color(0xFF607E2E) : const Color(0xFF75827C)))]));
}