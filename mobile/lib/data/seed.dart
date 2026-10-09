import 'package:flutter/material.dart';
import 'models.dart';

// Pastel product-photo backgrounds from the Electric Yellow reference.
const _pink = Color(0xFFFFC4E1);
const _yellow = Color(0xFFFFEEAA);
const _mint = Color(0xFFB8FFD2);
const _lavender = Color(0xFFE0D9FF);

const categories = <Category>[
  Category('vegetables', 'Veggies', Icons.eco_outlined),
  Category('fruits', 'Fruits', Icons.apple),
  Category('dairy', 'Dairy', Icons.local_drink_outlined),
  Category('pantry', 'Grocery', Icons.shopping_basket_outlined),
  Category('snacks', 'Snacks', Icons.cookie_outlined),
];

// Pack sizes of the loose-weight seed products, shaped like the API's variants.
const _tomatoPacks = [
  PackOption(id: 'tomato:250g', label: '250 g', price: 8, mrp: 11),
  PackOption(id: 'tomato:500g', label: '500 g', price: 15, mrp: 19),
  PackOption(id: 'tomato:1kg', label: '1 kg', price: 28, mrp: 36, isDefault: true),
];
const _onionPacks = [
  PackOption(id: 'onion:250g', label: '250 g', price: 8, mrp: 8),
  PackOption(id: 'onion:500g', label: '500 g', price: 14, mrp: 14),
  PackOption(id: 'onion:1kg', label: '1 kg', price: 26, mrp: 26, isDefault: true),
];
const _potatoPacks = [
  PackOption(id: 'potato:250g', label: '250 g', price: 9, mrp: 10),
  PackOption(id: 'potato:500g', label: '500 g', price: 16, mrp: 18),
  PackOption(id: 'potato:1kg', label: '1 kg', price: 30, mrp: 34, isDefault: true),
];
const _applePacks = [
  PackOption(id: 'apple:250g', label: '250 g', price: 42, mrp: 51),
  PackOption(id: 'apple:500g', label: '500 g', price: 78, mrp: 94),
  PackOption(id: 'apple:1kg', label: '1 kg', price: 149, mrp: 180, isDefault: true),
];
const _ricePacks = [
  PackOption(id: 'rice:250g', label: '250 g', price: 25, mrp: 28),
  PackOption(id: 'rice:500g', label: '500 g', price: 47, mrp: 52),
  PackOption(id: 'rice:1kg', label: '1 kg', price: 89, mrp: 99, isDefault: true),
];

const products = [
  Product(id: 'tomato', name: 'Tomato', nameMr: 'टोमॅटो', unit: '1 kg', price: 28, mrp: 36, color: _pink, category: 'vegetables', packs: _tomatoPacks, description: 'Vine-ripened tomatoes, firm and juicy. Great for curries, salads and chutneys.'),
  Product(id: 'onion', name: 'Onion', nameMr: 'कांदा', unit: '1 kg', price: 26, mrp: 26, color: _yellow, category: 'vegetables', packs: _onionPacks, description: 'Fresh red onions with a sharp, sweet bite. A kitchen staple.'),
  Product(id: 'potato', name: 'Potato', nameMr: 'बटाटा', unit: '1 kg', price: 30, mrp: 34, color: _mint, category: 'vegetables', packs: _potatoPacks, description: 'Clean, firm potatoes for fries, curries and everything in between.'),
  Product(id: 'carrot', name: 'Carrots', nameMr: 'गाजर', unit: '500 g', price: 38, mrp: 38, color: _lavender, category: 'vegetables', description: 'Sweet, crunchy carrots. Perfect raw, grated or roasted.'),
  Product(id: 'apple', name: 'Royal Apples', nameMr: 'सफरचंद', unit: '1 kg', price: 149, mrp: 180, color: _pink, category: 'fruits', packs: _applePacks, rating: 4.6, description: 'Crisp, sweet royal gala apples. Handpicked and sorted by size.'),
  Product(id: 'banana', name: 'Bananas', nameMr: 'केळी', unit: '6 pcs', price: 42, mrp: 48, color: _yellow, category: 'fruits', rating: 4.3, description: 'Naturally ripened bananas, ready to eat.'),
  Product(id: 'milk', name: 'Farm Milk', nameMr: 'दूध', unit: '500 ml', price: 34, mrp: 34, color: _lavender, category: 'dairy', rating: 4.7, description: 'Pasteurised full-cream milk from local farms, delivered chilled.'),
  Product(id: 'curd', name: 'Fresh Curd', nameMr: 'दही', unit: '400 g', price: 40, mrp: 40, color: _mint, category: 'dairy', inStock: false, description: 'Thick, creamy set curd made daily.'),
  Product(id: 'rice', name: 'Daily Rice', nameMr: 'तांदूळ', unit: '1 kg', price: 89, mrp: 99, color: _yellow, category: 'pantry', packs: _ricePacks, rating: 4.4, description: 'Everyday long-grain rice. Cooks fluffy and non-sticky.'),
  Product(id: 'cookies', name: 'Butter Cookies', nameMr: 'बटर कुकीज', unit: '200 g', price: 60, mrp: 75, color: _pink, category: 'snacks', rating: 4.2, description: 'Crumbly butter cookies baked fresh every week.'),
];

Product productById(String id) => products.firstWhere((p) => p.id == id);

Category categoryById(String id) => categories.firstWhere((c) => c.id == id);

/// Placeholder store used for "Sold by" and the ETA card until the API provides it.
const storeName = 'Sharma Fresh Mart';
const storeDistanceKm = 1.2;
const etaMinutes = 12;

const freeDeliveryThreshold = 199;
const deliveryFee = 25;
const handlingFee = 5;
